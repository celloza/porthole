using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json;
using Microsoft.WSL.Containers;
using Porthole.Core.Models;
using Porthole.Core.Services;

namespace Porthole.Tray.Services;

internal sealed class WslcBackendService : IDisposable, IDockerApiBackend
{
    private const int ContainerStateRunning = 2;
    private const string DefaultActiveSessionName = "porthole-devcontainers";
    private const string UnnamedDefaultSessionName = "(Default)";
    private const int HrFileExists = unchecked((int)0x800700B7);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly string DefaultBaseStoragePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Porthole",
        "Sessions");

    private static readonly string DevContainerStatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Porthole",
        "DevContainers",
        "containers.json");

    private static string GetDefaultSessionRegistryPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Porthole",
        "sessions.json");

    private readonly object _syncLock = new();
    private readonly Dictionary<string, Session> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _sessionSettings = new(StringComparer.OrdinalIgnoreCase); // name -> storagePath
    private readonly Dictionary<string, string> _sessionStatuses = new(StringComparer.OrdinalIgnoreCase); // name -> "Running"|"Stopped"
    private readonly string _baseStoragePath;
    private readonly string _sessionRegistryPath;
    private readonly bool _skipDefaultSessionDetection;
    private string _activeSessionName = DefaultActiveSessionName;
    private NetworkMode _networkMode = NetworkMode.Bridge;

    public WslcBackendService(string? baseStoragePath = null, string? registryPath = null, bool skipDefaultSessionDetection = false)
    {
        _baseStoragePath = baseStoragePath ?? DefaultBaseStoragePath;
        _sessionRegistryPath = registryPath ?? GetDefaultSessionRegistryPath();
        _skipDefaultSessionDetection = skipDefaultSessionDetection;
        InitializeFromRegistry();
    }

    public IReadOnlyList<SessionSummary> ListSessions()
    {
        lock (_syncLock)
        {
            return GetKnownSessionNamesLocked()
                .Select(name => new SessionSummary(
                    name,
                    GetSessionStoragePath(name),
                    string.Equals(name, _activeSessionName, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public void CreateNamedSession(string name)
    {
        lock (_syncLock)
        {
            EnsureSessionInitialized(name);
            _sessionStatuses[name] = "Running";
            SaveRegistryLocked();
        }
    }

    public void DeleteNamedSession(string name)
    {
        lock (_syncLock)
        {
            if (string.Equals(name, UnnamedDefaultSessionName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Cannot delete the default unnamed session '{name}'.");
            }

            if (string.Equals(name, _activeSessionName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Cannot delete the active session '{name}'. Switch to another session first.");
            }

            if (_sessions.TryGetValue(name, out Session? session))
            {
                _sessions.Remove(name);
                try { session.Terminate(); } catch { }
                session.Dispose();
            }

            _sessionSettings.Remove(name);
            _sessionStatuses.Remove(name);
            SaveRegistryLocked();
        }
    }

    public void PauseSession(string name)
    {
        lock (_syncLock)
        {
            string normalizedName = name.Trim();
            if (string.Equals(normalizedName, UnnamedDefaultSessionName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Cannot pause the default unnamed session '{normalizedName}'.");
            }

            if (!_sessions.TryGetValue(normalizedName, out Session? session))
            {
                throw new InvalidOperationException($"Session '{normalizedName}' does not exist.");
            }

            // Terminate the WSL VM to free resources; settings and VHD are preserved for Resume.
            try { session.Terminate(); } catch { }
            session.Dispose();
            _sessions.Remove(normalizedName);
            _sessionStatuses[normalizedName] = "Stopped";

            // If this was the active session, prefer switching to another running session.
            // If none are available, keep _activeSessionName pointing to the paused session name
            // so GetActiveSessionInstance() auto-recreates it on the next operation that needs it.
            if (string.Equals(normalizedName, _activeSessionName, StringComparison.OrdinalIgnoreCase))
            {
                _activeSessionName = _sessions.Keys.FirstOrDefault() ?? normalizedName;
            }

            SaveRegistryLocked();
        }
    }

    public void ResumeSession(string name)
    {
        lock (_syncLock)
        {
            string normalizedName = name.Trim();
            if (string.Equals(normalizedName, UnnamedDefaultSessionName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Cannot resume the default unnamed session '{normalizedName}'.");
            }

            if (_sessions.ContainsKey(normalizedName))
            {
                // Already running; update status in case it drifted.
                _sessionStatuses[normalizedName] = "Running";
                return;
            }

            if (!_sessionSettings.ContainsKey(normalizedName))
            {
                throw new InvalidOperationException($"Session '{normalizedName}' settings not found. The session may have been terminated.");
            }

            EnsureSessionInitialized(normalizedName);
            _sessionStatuses[normalizedName] = "Running";
            SaveRegistryLocked();
        }
    }

    public void TerminateNamedSession(string name)
    {
        lock (_syncLock)
        {
            string normalizedName = name.Trim();
            if (string.Equals(normalizedName, UnnamedDefaultSessionName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Cannot terminate the default unnamed session '{normalizedName}'.");
            }

            if (_sessions.TryGetValue(normalizedName, out Session? session))
            {
                _sessions.Remove(normalizedName);
                try { session.Terminate(); } catch { }
                session.Dispose();
            }

            _sessionSettings.Remove(normalizedName);
            _sessionStatuses.Remove(normalizedName);

            if (string.Equals(normalizedName, _activeSessionName, StringComparison.OrdinalIgnoreCase))
            {
                _activeSessionName = GetFallbackActiveSessionNameLocked(normalizedName);
                EnsureSessionInitialized(_activeSessionName);
            }

            SaveRegistryLocked();
        }
    }

    public IReadOnlyList<SessionSnapshot> GetTraySnapshot()
    {
        lock (_syncLock)
        {
            // Use the same name source as ListSessions() so all known sessions are visible,
            // including the (Default) session (which is never added to _sessions) and
            // non-active sessions that have not been started since the last tray boot.
            var allNames = new HashSet<string>(GetKnownSessionNamesLocked(), StringComparer.OrdinalIgnoreCase);
            allNames.UnionWith(_sessions.Keys);
            allNames.UnionWith(_sessionStatuses.Keys);

            return allNames
                .Select(name =>
                {
                    string status = _sessionStatuses.TryGetValue(name, out string? s)
                        ? s
                        : _sessions.ContainsKey(name) ? "Running" : "Stopped";

                    return new SessionSnapshot(
                        name,
                        string.Equals(name, _activeSessionName, StringComparison.OrdinalIgnoreCase),
                        status,
                        CpuUsage: string.Empty,
                        MemoryUsage: string.Empty);
                })
                .OrderByDescending(s => s.IsActive)
                .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public void SetActiveSession(string name)
    {
        lock (_syncLock)
        {
            EnsureSessionInitialized(name);
            _activeSessionName = name;
            SaveRegistryLocked();
        }
    }

    public string GetActiveSessionName()
    {
        lock (_syncLock)
        {
            return _activeSessionName;
        }
    }

    public async Task<NetworkingSnapshot> GetNetworkingSnapshotAsync(CancellationToken cancellationToken = default)
    {
        string? httpProxy = Environment.GetEnvironmentVariable("HTTP_PROXY")
            ?? Environment.GetEnvironmentVariable("http_proxy");
        string? httpsProxy = Environment.GetEnvironmentVariable("HTTPS_PROXY")
            ?? Environment.GetEnvironmentVariable("https_proxy");
        string? noProxy = Environment.GetEnvironmentVariable("NO_PROXY")
            ?? Environment.GetEnvironmentVariable("no_proxy");

        NetworkMode mode;
        IReadOnlyList<PortBinding> portBindings;

        lock (_syncLock)
        {
            mode = _networkMode;
        }

        try
        {
            portBindings = await EnumeratePortBindingsAsync(cancellationToken);
        }
        catch
        {
            portBindings = [];
        }

        return new NetworkingSnapshot(
            mode,
            portBindings,
            new ProxyConfiguration(httpProxy, httpsProxy, noProxy));
    }

    private async Task<IReadOnlyList<PortBinding>> EnumeratePortBindingsAsync(CancellationToken cancellationToken)
    {
        var bindings = new List<PortBinding>();

        try
        {
            // Get list of running containers
            IReadOnlyList<ContainerListItem> containers = await GetContainersAsync(cancellationToken);

            // For each running container, inspect it to get port bindings
            foreach (ContainerListItem container in containers)
            {
                if (container.State != 2)
                {
                    // Skip non-running containers (State 2 = running)
                    continue;
                }

                try
                {
                    string json = await RunWslcActiveSessionCommandAsync($"inspect {EscapeCliArgument(container.Name)}", cancellationToken);
                    using JsonDocument doc = JsonDocument.Parse(json);

                    // wslc inspect returns an array with one element
                    if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                    {
                        JsonElement containerElem = doc.RootElement[0];
                        string? containerId = containerElem.GetProperty("Id").GetString();
                        string? containerName = containerElem.GetProperty("Name").GetString();

                        // Extract port mappings from Ports property (top-level on container)
                        if (containerElem.TryGetProperty("Ports", out JsonElement ports)
                            && ports.ValueKind == JsonValueKind.Object)
                        {
                            foreach (JsonProperty portMapping in ports.EnumerateObject())
                            {
                                // Format: "80/tcp" -> parse the container port
                                string[] parts = portMapping.Name.Split('/');
                                if (parts.Length == 2
                                    && int.TryParse(parts[0], out int containerPort))
                                {
                                    string protocol = parts[1];

                                    // Extract host port from bindings array
                                    if (portMapping.Value.ValueKind == JsonValueKind.Array
                                        && portMapping.Value.GetArrayLength() > 0)
                                    {
                                        JsonElement firstBinding = portMapping.Value[0];
                                        if (firstBinding.TryGetProperty("HostPort", out JsonElement hostPortElem)
                                            && int.TryParse(hostPortElem.GetString(), out int hostPort))
                                        {
                                            bindings.Add(new PortBinding(
                                                containerId ?? "unknown",
                                                containerName ?? "unknown",
                                                hostPort,
                                                containerPort,
                                                protocol));
                                        }
                                    }
                                }
                            }
                    }
                }
                }
                catch
                {
                    // Skip containers that fail to inspect
                }
            }
        }
        catch
        {
            // If enumerate fails, return empty list
        }

        return bindings;
    }

    public void SetNetworkMode(NetworkMode mode)
    {
        lock (_syncLock)
        {
            _networkMode = mode;
        }
    }

    public SessionSettings CreateDefaultSessionSettings(string sessionName)
    {
        Directory.CreateDirectory(_baseStoragePath);

        string sessionPath = Path.Combine(_baseStoragePath, sessionName);
        Directory.CreateDirectory(sessionPath);

        return new SessionSettings(sessionName, sessionPath)
        {
            CpuCount = 4,
            MemorySizeInMB = 4096,
            VhdRequirements = new VhdOptions(string.Empty, 8UL * 1024 * 1024 * 1024, VhdType.Dynamic),
        };
    }

    public Session StartSession(string sessionName)
    {
        var session = new Session(CreateDefaultSessionSettings(sessionName));
        StartSessionIfNeeded(session);
        return session;
    }

    public Task<IReadOnlyList<ImageSummary>> ListImagesAsync(CancellationToken cancellationToken = default)
    {
        return ListImagesCoreAsync(cancellationToken);
    }

    public async Task<DockerImageDetails> GetImageDetailsAsync(string imageReference, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(imageReference))
        {
            throw new InvalidOperationException("Image reference is required.");
        }

        string trimmedReference = imageReference.Trim();
        IReadOnlyList<CliImageListItem> images = await ListImageItemsAsync(cancellationToken);
        CliImageListItem? image = images.FirstOrDefault(candidate => ImageMatches(candidate, trimmedReference));

        if (image is null)
        {
            throw new InvalidOperationException($"Image not found: {trimmedReference}");
        }

        string reference = BuildImageReference(image.Repository, image.Tag);
        SplitReference(reference, out string repository, out string tag);
        string digest = string.IsNullOrWhiteSpace(image.Id) ? reference : image.Id;

        return new DockerImageDetails(
            digest,
            repository,
            tag,
            reference,
            FromUnixTimeSecondsOrNow(image.Created),
            image.Size);
    }

    private async Task<IReadOnlyList<ImageSummary>> ListImagesCoreAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<CliImageListItem> images = await ListImageItemsAsync(cancellationToken);
        return images
            .Select(ToImageSummary)
            .OrderBy(image => image.Repository, StringComparer.OrdinalIgnoreCase)
            .ThenBy(image => image.Tag, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<IReadOnlyList<CliImageListItem>> ListImageItemsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string json = await RunWslcActiveSessionCommandAsync("images --format json", cancellationToken);
        IReadOnlyList<CliImageListItem>? images = JsonSerializer.Deserialize<IReadOnlyList<CliImageListItem>>(json, JsonOptions);
        return images ?? [];
    }

    public async Task<IReadOnlyList<ContainerSummary>> ListContainersAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        bool useDevContainerFallback;
        lock (_syncLock)
        {
            useDevContainerFallback = string.Equals(_activeSessionName, DefaultActiveSessionName, StringComparison.OrdinalIgnoreCase);
        }

            IReadOnlyList<ContainerListItem> containers = await GetContainersAsync(cancellationToken);

        if (containers.Count == 0 && useDevContainerFallback)
        {
            return LoadDevContainerStateSummaries();
        }

        ContainerSummary[] mapped = containers
            .Select(container => new ContainerSummary(
                container.Id,
                container.Name,
                container.Image,
                container.State,
                ToContainerStateText(container.State),
                FromUnixTimeSecondsOrNow(container.CreatedAt)))
            .OrderByDescending(container => container.IsRunning)
            .ThenBy(container => container.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return mapped;
    }

    public async Task<string> InspectContainerJsonAsync(string containerReference, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(containerReference))
        {
            throw new InvalidOperationException("Container reference is required.");
        }

        try
        {
            string json = await RunWslcActiveSessionCommandAsync($"inspect {EscapeCliArgument(containerReference)}", cancellationToken);
            using JsonDocument document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind == JsonValueKind.Array && document.RootElement.GetArrayLength() > 0)
            {
                return document.RootElement[0].GetRawText();
            }

            return document.RootElement.GetRawText();
        }
        catch when (TryGetDevContainerStateRecord(containerReference, out StoredDevContainerRecord? record) && record is not null)
        {
            return record.InspectJson;
        }
    }

    public async Task<string> GetContainerLogsAsync(
        string containerReference,
        string? tail,
        bool timestamps,
        string? since,
        string? until,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(containerReference))
        {
            throw new InvalidOperationException("Container reference is required.");
        }

        var arguments = new StringBuilder();
        arguments.Append("logs ");

        if (!string.IsNullOrWhiteSpace(tail) && !string.Equals(tail, "all", StringComparison.OrdinalIgnoreCase))
        {
            arguments.Append("--tail ");
            arguments.Append(EscapeCliArgument(tail.Trim()));
            arguments.Append(' ');
        }

        if (timestamps)
        {
            arguments.Append("--timestamps ");
        }

        if (!string.IsNullOrWhiteSpace(since))
        {
            arguments.Append("--since ");
            arguments.Append(EscapeCliArgument(since.Trim()));
            arguments.Append(' ');
        }

        if (!string.IsNullOrWhiteSpace(until))
        {
            arguments.Append("--until ");
            arguments.Append(EscapeCliArgument(until.Trim()));
            arguments.Append(' ');
        }

        arguments.Append(EscapeCliArgument(containerReference.Trim()));

        try
        {
            return await RunWslcActiveSessionCommandAsync(arguments.ToString(), cancellationToken);
        }
        catch when (TryGetDevContainerStateRecord(containerReference, out StoredDevContainerRecord? _))
        {
            return string.Empty;
        }
    }

    public async Task StreamContainerLogsAsync(
        string containerReference,
        Stream destination,
        string? tail,
        bool timestamps,
        string? since,
        string? until,
        bool follow,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(containerReference))
        {
            throw new InvalidOperationException("Container reference is required.");
        }

        using System.Diagnostics.Process process = StartProcessCommand(
            "wslc",
            BuildSessionScopedWslcArguments(GetActiveSessionNameSnapshot(), BuildContainerLogsArguments(containerReference, tail, timestamps, since, until, follow)));
        Task<string> standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.StandardOutput.BaseStream.CopyToAsync(destination, cancellationToken);
            await destination.FlushAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
        }
        catch
        {
            TryTerminateProcess(process);
            throw;
        }

        string standardError = await standardErrorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(standardError)
                ? $"wslc logs failed with exit code {process.ExitCode}."
                : standardError.Trim());
        }
    }

    private static IReadOnlyList<ContainerSummary> LoadDevContainerStateSummaries()
    {
        return LoadDevContainerStateRecords()
            .Select(record => BuildContainerSummary(record))
            .OrderByDescending(container => container.IsRunning)
            .ThenBy(container => container.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static ContainerSummary BuildContainerSummary(StoredDevContainerRecord record)
    {
        using JsonDocument document = JsonDocument.Parse(record.InspectJson);
        JsonElement root = document.RootElement;

        string image = root.TryGetProperty("Image", out JsonElement imageElement)
            ? imageElement.GetString() ?? string.Empty
            : string.Empty;

        string stateText = "unknown";
        bool isRunning = false;
        if (root.TryGetProperty("State", out JsonElement stateElement) && stateElement.ValueKind == JsonValueKind.Object)
        {
            if (stateElement.TryGetProperty("Running", out JsonElement runningElement)
                && runningElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                isRunning = runningElement.GetBoolean();
            }

            if (stateElement.TryGetProperty("Status", out JsonElement statusElement))
            {
                stateText = statusElement.GetString() ?? stateText;
            }
            else if (isRunning)
            {
                stateText = "running";
            }
        }

        return new ContainerSummary(
            record.Id,
            record.Name ?? string.Empty,
            image,
            isRunning ? ContainerStateRunning : 0,
            stateText,
            TryParseCreatedAt(root, record.UpdatedAtUtc));
    }

    private static bool TryGetDevContainerStateRecord(string containerReference, out StoredDevContainerRecord? record)
    {
        record = LoadDevContainerStateRecords()
            .FirstOrDefault(item =>
                string.Equals(item.Id, containerReference, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(item.Name)
                    && string.Equals(item.Name, containerReference, StringComparison.OrdinalIgnoreCase)));

        return record is not null;
    }

    private static IReadOnlyList<StoredDevContainerRecord> LoadDevContainerStateRecords()
    {
        if (!File.Exists(DevContainerStatePath))
        {
            return [];
        }

        try
        {
            string json = File.ReadAllText(DevContainerStatePath);
            return JsonSerializer.Deserialize<List<StoredDevContainerRecord>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task<string> CreateDockerContainerAsync(
        string image,
        string? name,
        IReadOnlyList<string>? command,
        IReadOnlyList<string>? environment,
        IReadOnlyList<string>? binds,
        IReadOnlyList<string>? portMappings,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var config = new ContainerConfig(
            Name: string.IsNullOrWhiteSpace(name) ? $"porthole-{Guid.NewGuid():N}"[..21] : name.Trim(),
            ImageReference: image,
            StartupCommand: command is null || command.Count == 0 ? null : string.Join(' ', command),
            PortMappings: portMappings,
            EnvironmentVariables: environment,
            VolumeMounts: binds);

        return await CreateContainerAsync(config, cancellationToken);
    }

    public async Task<DockerExecResult> ExecContainerAsync(
        string containerReference,
        IReadOnlyList<string> command,
        string? workingDirectory,
        IReadOnlyList<string>? environment,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(containerReference))
        {
            throw new InvalidOperationException("Container reference is required.");
        }

        if (command.Count == 0)
        {
            throw new InvalidOperationException("A command is required.");
        }

        string args = $"exec {EscapeCliArgument(containerReference)}";

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            args += $" --workdir {EscapeCliArgument(workingDirectory.Trim())}";
        }

        if (environment is not null)
        {
            foreach (string env in environment)
            {
                if (!string.IsNullOrWhiteSpace(env))
                {
                    args += $" --env {EscapeCliArgument(env.Trim())}";
                }
            }
        }

        foreach (string arg in command)
        {
            args += $" {EscapeCliArgument(arg)}";
        }

        try
        {
            string output = await RunWslcActiveSessionCommandAsync(args, cancellationToken);
            return new DockerExecResult(0, output, string.Empty);
        }
        catch when (TryGetDevContainerStateRecord(containerReference, out StoredDevContainerRecord? record) && record is not null)
        {
            return await ExecStoredDevContainerAsync(record, command, workingDirectory, environment, cancellationToken);
        }
    }

    private async Task<DockerExecResult> ExecStoredDevContainerAsync(
        StoredDevContainerRecord record,
        IReadOnlyList<string> command,
        string? workingDirectory,
        IReadOnlyList<string>? environment,
        CancellationToken cancellationToken)
    {
        string storagePath = Path.Combine(_baseStoragePath, DefaultActiveSessionName);
        Directory.CreateDirectory(storagePath);

        var sessionSettings = new SessionSettings(DefaultActiveSessionName, storagePath);
        using var session = new Session(sessionSettings);
        session.Start();

        var containerSettings = new ContainerSettings(GetImageFromInspect(record.InspectJson))
        {
            Name = string.IsNullOrWhiteSpace(record.Name) ? record.Id[..12] : record.Name,
        };

        using var containerHandle = session.CreateContainer(containerSettings);

        var processSettings = new ProcessSettings
        {
            CommandLine = command.ToArray(),
            OutputMode = ProcessOutputMode.Stream,
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            processSettings.WorkingDirectory = workingDirectory.Trim();
        }

        if (environment is not null)
        {
            foreach (string env in environment)
            {
                int separatorIndex = env.IndexOf('=', StringComparison.Ordinal);
                if (separatorIndex > 0)
                {
                    processSettings.EnvironmentVariables[env[..separatorIndex]] = env[(separatorIndex + 1)..];
                }
            }
        }

        using var process = containerHandle.CreateProcess(processSettings);
        var standardOutput = new StringBuilder();
        var standardError = new StringBuilder();
        var exited = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        process.OutputReceived += data => standardOutput.Append(Encoding.UTF8.GetString(data));
        process.ErrorReceived += data => standardError.Append(Encoding.UTF8.GetString(data));
        process.Exited += exitCode => exited.TrySetResult(exitCode);

        using CancellationTokenRegistration registration = cancellationToken.Register(() => exited.TrySetCanceled(cancellationToken));

        process.Start();
        int code = await exited.Task;
        return new DockerExecResult(code, standardOutput.ToString(), standardError.ToString());
    }

    public async Task<IReadOnlyList<PodSummary>> ListPodsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            string json = await RunProcessCommandAsync("kubectl", "get pods -A -o json", cancellationToken);
            var document = JsonSerializer.Deserialize<KubectlPodListResponse>(json, JsonOptions);
            if (document?.Items is null)
            {
                return [];
            }

            PodSummary[] pods = document.Items
                .Select(item => new PodSummary(
                    item.Metadata?.Namespace ?? "default",
                    item.Metadata?.Name ?? "unknown",
                    item.Status?.Phase ?? "Unknown",
                    item.Spec?.NodeName ?? string.Empty))
                .OrderBy(pod => pod.Namespace, StringComparer.OrdinalIgnoreCase)
                .ThenBy(pod => pod.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return pods;
        }
        catch
        {
            return [];
        }
    }

    public async Task StartContainerAsync(string containerReference, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(containerReference))
        {
            throw new InvalidOperationException("Container reference is required.");
        }

        await RunWslcActiveSessionCommandAsync($"start {EscapeCliArgument(containerReference)}", cancellationToken);
    }

    public async Task StopContainerAsync(string containerReference, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(containerReference))
        {
            throw new InvalidOperationException("Container reference is required.");
        }

        await RunWslcActiveSessionCommandAsync($"stop {EscapeCliArgument(containerReference)}", cancellationToken);
    }

    public async Task RemoveContainerAsync(string containerReference, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(containerReference))
        {
            throw new InvalidOperationException("Container reference is required.");
        }

        await RunWslcActiveSessionCommandAsync($"remove {EscapeCliArgument(containerReference)}", cancellationToken);
    }

    public async Task<DashboardSnapshot> GetDashboardSnapshotAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<ContainerListItem> containers = await GetContainersAsync(cancellationToken);
        int runningCount = containers.Count(container => container.State == ContainerStateRunning);
        int stoppedCount = containers.Count - runningCount;

        string cpuUsageText;
        string memoryUsageText;
        string sessionStatus;
        double cpuPercent = 0;
        double memoryPercent = 0;

        if (runningCount == 0)
        {
            cpuUsageText = "Idle";
            memoryUsageText = "Idle";
            sessionStatus = containers.Count == 0
                ? "WSL Containers is connected. No containers have been created yet."
                : "WSL Containers is connected. Containers exist, but none are currently running.";
        }
        else
        {
            try
            {
                IReadOnlyList<ContainerStatsItem> stats = await GetContainerStatsAsync(cancellationToken);
                cpuUsageText = FormatCpuUsage(stats);
                memoryUsageText = FormatMemoryUsage(stats);
                cpuPercent = ComputeCpuPercent(stats);
                memoryPercent = ComputeMemoryPercent(stats);
                System.Diagnostics.Debug.WriteLine($"[WslcBackendService] Computed cpuPercent={cpuPercent}, memoryPercent={memoryPercent}, statsCount={stats.Count}");
                sessionStatus = $"WSL Containers is connected. {runningCount} container{(runningCount == 1 ? string.Empty : "s")} running.";
            }
            catch
            {
                cpuUsageText = "Telemetry unavailable";
                memoryUsageText = "Telemetry unavailable";
                sessionStatus = $"WSL Containers is connected. {runningCount} container{(runningCount == 1 ? string.Empty : "s")} running, but stats could not be loaded.";
            }
        }

        return new DashboardSnapshot(
            cpuUsageText,
            memoryUsageText,
            $"{runningCount} running",
            sessionStatus)
        {
            CpuPercent = cpuPercent,
            MemoryPercent = memoryPercent,
        };
    }

    public async Task PullImageAsync(string imageReference, IProgress<ImagePullProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var session = GetActiveSessionInstance();
        var operation = session.PullImageAsync(new PullImageOptions(imageReference));
        var progressAdapter = new Progress<Microsoft.WSL.Containers.ImageProgress>(update =>
        {
            int percent = update.TotalBytes == 0 ? 0 : (int)Math.Clamp((update.CurrentBytes * 100UL) / update.TotalBytes, 0UL, 100UL);
            progress?.Report(new ImagePullProgress(percent, $"{update.Status}: {update.Id} ({percent}%)"));
        });

        await operation.AsTask(cancellationToken, progressAdapter);
    }

    public Task DeleteImageAsync(string imageReference, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GetActiveSessionInstance().DeleteImage(imageReference);
        return Task.CompletedTask;
    }

    public Task TagImageAsync(string sourceImageReference, string newTag, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        SplitReference(newTag, out string repository, out string tag);
        GetActiveSessionInstance().TagImage(new TagImageOptions(sourceImageReference, repository, tag));

        return Task.CompletedTask;
    }

    public Task PruneImagesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotSupportedException("The current Microsoft.WSL.Containers SDK does not expose an image prune API.");
    }

    public async Task<string> CreateContainerAsync(ContainerConfig config, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(config.Name))
        {
            throw new InvalidOperationException("Container name is required.");
        }

        if (string.IsNullOrWhiteSpace(config.ImageReference))
        {
            throw new InvalidOperationException("Image reference is required.");
        }

        var args = new System.Text.StringBuilder("run --detach");

        args.Append(" --name ");
        args.Append(EscapeCliArgument(config.Name.Trim()));

        if (config.PortMappings is not null)
        {
            foreach (string port in config.PortMappings)
            {
                if (!string.IsNullOrWhiteSpace(port))
                {
                    args.Append(" --publish ");
                    args.Append(EscapeCliArgument(port.Trim()));
                }
            }
        }

        if (config.EnvironmentVariables is not null)
        {
            foreach (string env in config.EnvironmentVariables)
            {
                if (!string.IsNullOrWhiteSpace(env))
                {
                    args.Append(" --env ");
                    args.Append(EscapeCliArgument(env.Trim()));
                }
            }
        }

        if (config.VolumeMounts is not null)
        {
            foreach (string vol in config.VolumeMounts)
            {
                if (!string.IsNullOrWhiteSpace(vol))
                {
                    args.Append(" --volume ");
                    args.Append(EscapeCliArgument(vol.Trim()));
                }
            }
        }

        args.Append(' ');
        args.Append(EscapeCliArgument(config.ImageReference.Trim()));

        if (!string.IsNullOrWhiteSpace(config.StartupCommand))
        {
            foreach (string startupArg in SplitCommandLineArguments(config.StartupCommand.Trim()))
            {
                args.Append(' ');
                args.Append(EscapeCliArgument(startupArg));
            }
        }

        string output = await RunWslcActiveSessionCommandAsync(args.ToString(), cancellationToken);
        return output.Trim();
    }

    public async Task<IReadOnlyList<VolumeSummary>> ListVolumesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string json = await RunWslcActiveSessionCommandAsync("volume ls --format json", cancellationToken);
        var items = JsonSerializer.Deserialize<List<VolumeListItem>>(json, JsonOptions) ?? [];
        MountSnapshot mountSnapshot = await GetMountTelemetryAsync(cancellationToken);

        var volumes = new Dictionary<string, VolumeSummary>(StringComparer.OrdinalIgnoreCase);

        foreach (VolumeListItem volume in items)
        {
            string name = volume.Name ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            mountSnapshot.NamedVolumes.TryGetValue(name, out MountTelemetry? mount);
            volumes[name] = new VolumeSummary(
                name,
                string.IsNullOrWhiteSpace(volume.Driver) ? mount?.Driver ?? "local" : volume.Driver,
                mount?.Destination ?? string.Empty,
                null,
                volume.UsageData?.Size is long s && s > 0 ? ToSizeLabel((ulong)s) : "unknown",
                mount?.IsInUse ?? false,
                false,
                mount?.IsReadOnly ?? false,
                "session local",
                volume.Mountpoint,
                GetVolumeCreatedAtUtc(volume.Mountpoint, null));
        }

        foreach (MountTelemetry bindMount in mountSnapshot.BindMounts)
        {
            string key = $"bind::{bindMount.Source}::{bindMount.Destination}";
            volumes[key] = new VolumeSummary(
                bindMount.Name,
                string.IsNullOrWhiteSpace(bindMount.Driver) ? "virtiofs" : bindMount.Driver,
                bindMount.Destination,
                bindMount.Source,
                "host path",
                bindMount.IsInUse,
                true,
                bindMount.IsReadOnly,
                bindMount.ThroughputClass,
                null,
                GetVolumeCreatedAtUtc(null, bindMount.Source));
        }

        return volumes.Values
            .OrderByDescending(volume => volume.IsInUse)
            .ThenBy(volume => volume.IsBindMount)
            .ThenBy(volume => volume.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public Task<DevContainerCapabilityReport> AnalyzeDevContainerConfigAsync(string devContainerConfigJson, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(DevContainerConfigAnalyzer.Analyze(devContainerConfigJson));
    }

    public async Task CreateVolumeAsync(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Volume name is required.");
        }

        await RunWslcActiveSessionCommandAsync($"volume create {EscapeCliArgument(name.Trim())}", cancellationToken);
    }

    public async Task DeleteVolumeAsync(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Volume name is required.");
        }

        string trimmedName = name.Trim();
        MountSnapshot mountSnapshot = await GetMountTelemetryAsync(cancellationToken);
        if (mountSnapshot.NamedVolumes.TryGetValue(trimmedName, out MountTelemetry? mount) && mount.IsInUse)
        {
            throw new InvalidOperationException($"Volume '{trimmedName}' is still attached to a running container.");
        }

        await RunWslcActiveSessionCommandAsync($"volume rm {EscapeCliArgument(trimmedName)}", cancellationToken);
    }

    public async Task PruneVolumesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await RunWslcActiveSessionCommandAsync("volume prune --force", cancellationToken);
    }

    private async Task<MountSnapshot> GetMountTelemetryAsync(CancellationToken cancellationToken)
    {
        var namedVolumes = new Dictionary<string, MountTelemetry>(StringComparer.OrdinalIgnoreCase);
        var bindMounts = new List<MountTelemetry>();

        IReadOnlyList<ContainerListItem> containers = await GetContainersAsync(cancellationToken);
        foreach (ContainerListItem container in containers)
        {
            try
            {
                string json = await RunWslcActiveSessionCommandAsync($"inspect {EscapeCliArgument(container.Name)}", cancellationToken);
                using JsonDocument doc = JsonDocument.Parse(json);

                if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                {
                    continue;
                }

                JsonElement containerElem = doc.RootElement[0];
                if (!containerElem.TryGetProperty("Mounts", out JsonElement mounts)
                    || mounts.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (JsonElement mount in mounts.EnumerateArray())
                {
                    MountTelemetry? telemetry = TryParseMountTelemetry(mount, container.State == ContainerStateRunning);
                    if (telemetry is null)
                    {
                        continue;
                    }

                    if (telemetry.IsBindMount)
                    {
                        bindMounts.Add(telemetry);
                        continue;
                    }

                    if (namedVolumes.TryGetValue(telemetry.Name, out MountTelemetry? existing))
                    {
                        namedVolumes[telemetry.Name] = existing with
                        {
                            Destination = string.IsNullOrWhiteSpace(existing.Destination) ? telemetry.Destination : existing.Destination,
                            IsInUse = existing.IsInUse || telemetry.IsInUse,
                            IsReadOnly = existing.IsReadOnly && telemetry.IsReadOnly,
                        };
                    }
                    else
                    {
                        namedVolumes[telemetry.Name] = telemetry;
                    }
                }
            }
            catch
            {
                // Skip containers that fail to inspect and keep other mount telemetry visible.
            }
        }

        return new MountSnapshot(namedVolumes, bindMounts);
    }

    private static MountTelemetry? TryParseMountTelemetry(JsonElement mount, bool isInUse)
    {
        string type = mount.TryGetProperty("Type", out JsonElement typeElem)
            ? typeElem.GetString() ?? string.Empty
            : string.Empty;
        string name = mount.TryGetProperty("Name", out JsonElement nameElem)
            ? nameElem.GetString() ?? string.Empty
            : string.Empty;
        string source = mount.TryGetProperty("Source", out JsonElement sourceElem)
            ? sourceElem.GetString() ?? string.Empty
            : string.Empty;
        string destination = mount.TryGetProperty("Destination", out JsonElement destinationElem)
            ? destinationElem.GetString() ?? string.Empty
            : string.Empty;
        string driver = mount.TryGetProperty("Driver", out JsonElement driverElem)
            ? driverElem.GetString() ?? string.Empty
            : string.Empty;
        string mode = mount.TryGetProperty("Mode", out JsonElement modeElem)
            ? modeElem.GetString() ?? string.Empty
            : string.Empty;
        bool isReadOnly = mount.TryGetProperty("RW", out JsonElement rwElem)
            ? !rwElem.GetBoolean()
            : mode.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(option => string.Equals(option, "ro", StringComparison.OrdinalIgnoreCase));

        if (string.Equals(type, "bind", StringComparison.OrdinalIgnoreCase))
        {
            string bindName = Path.GetFileName(source.TrimEnd('\\', '/'));
            if (string.IsNullOrWhiteSpace(bindName))
            {
                bindName = source;
            }

            return new MountTelemetry(
                bindName,
                "virtiofs",
                source,
                destination,
                true,
                isReadOnly,
                isInUse,
                "shared memory");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return new MountTelemetry(
            name,
            string.IsNullOrWhiteSpace(driver) ? "local" : driver,
            source,
            destination,
            false,
            isReadOnly,
            isInUse,
            "session local");
    }

    private sealed record MountSnapshot(
        IReadOnlyDictionary<string, MountTelemetry> NamedVolumes,
        IReadOnlyList<MountTelemetry> BindMounts);

    private sealed record MountTelemetry(
        string Name,
        string Driver,
        string Source,
        string Destination,
        bool IsBindMount,
        bool IsReadOnly,
        bool IsInUse,
        string ThroughputClass);

    public void Dispose()
    {
        lock (_syncLock)
        {
            foreach (var session in _sessions.Values)
            {
                try { session.Terminate(); } catch { }
                session.Dispose();
            }

            _sessions.Clear();
        }
    }

    private Session GetActiveSessionInstance()
    {
        lock (_syncLock)
        {
            EnsureSessionInitialized(_activeSessionName);
            _sessionStatuses[_activeSessionName] = "Running";
            return _sessions[_activeSessionName];
        }
    }

    private void EnsureSessionInitialized(string sessionName)
    {
        if (string.IsNullOrWhiteSpace(sessionName))
        {
            throw new InvalidOperationException("Session name is required.");
        }

        string normalizedName = sessionName.Trim();

        // Default unnamed session is managed by WSL, no initialization needed
        if (normalizedName.Equals(UnnamedDefaultSessionName, StringComparison.OrdinalIgnoreCase))
        {
            _sessionSettings[normalizedName] = GetSessionStoragePath(normalizedName);
            return;
        }

        if (_sessions.ContainsKey(normalizedName))
        {
            if (!_sessionSettings.ContainsKey(normalizedName))
            {
                _sessionSettings[normalizedName] = GetSessionStoragePath(normalizedName);
            }

            return;
        }

        Directory.CreateDirectory(_baseStoragePath);
        var settings = CreateDefaultSessionSettings(normalizedName);
        var session = new Session(settings);
        StartSessionIfNeeded(session);
        _sessions[normalizedName] = session;
        _sessionSettings[normalizedName] = settings.StoragePath;
    }

    private void InitializeFromRegistry()
    {
        lock (_syncLock)
        {
            SessionRegistry? registry = TryLoadRegistryLocked();
            if (registry is { KnownSessionNames.Count: > 0 }
                && !string.IsNullOrWhiteSpace(registry.ActiveSessionName))
            {
                foreach (string name in registry.KnownSessionNames.Where(n => !string.IsNullOrWhiteSpace(n)))
                {
                    _sessionSettings[name] = GetSessionStoragePath(name);
                }

                _activeSessionName = registry.ActiveSessionName;
                EnsureSessionInitialized(_activeSessionName);
                return;
            }

            // First run or migration: discover existing sessions from the filesystem.
            var discoveredNames = new List<string>();
            if (Directory.Exists(_baseStoragePath))
            {
                foreach (string directory in Directory.EnumerateDirectories(_baseStoragePath))
                {
                    string? name = Path.GetFileName(directory);
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        discoveredNames.Add(name);
                    }
                }
            }

            if (discoveredNames.Count > 0)
            {
                // Migration path: register all discovered sessions and prefer any
                // pre-existing session over the new default to avoid the regression
                // where all existing containers appear to vanish after an upgrade.
                // discoveredNames only contains non-null/whitespace names (filtered above),
                // so ordered will also be non-empty.
                var ordered = discoveredNames
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // Also check for the unnamed default session with containers
                if (!_skipDefaultSessionDetection && HasDefaultUnnamedSessionOnFirstRun())
                {
                    if (!ordered.Contains(UnnamedDefaultSessionName, StringComparer.OrdinalIgnoreCase))
                    {
                        ordered.Insert(0, UnnamedDefaultSessionName);
                    }
                }

                foreach (string name in ordered)
                {
                    _sessionSettings[name] = GetSessionStoragePath(name);
                }

                // Prefer: other named sessions > default unnamed session > porthole-devcontainers
                _activeSessionName = ordered.FirstOrDefault(
                    n => !n.Equals(DefaultActiveSessionName, StringComparison.OrdinalIgnoreCase)
                         && !n.Equals(UnnamedDefaultSessionName, StringComparison.OrdinalIgnoreCase))
                    ?? ordered.FirstOrDefault(n => n.Equals(UnnamedDefaultSessionName, StringComparison.OrdinalIgnoreCase))
                    ?? ordered.FirstOrDefault()
                    ?? DefaultActiveSessionName;
            }
            else
            {
                // Fresh install: check if there's a default unnamed session with containers
                if (!_skipDefaultSessionDetection && HasDefaultUnnamedSessionOnFirstRun())
                {
                    _sessionSettings[UnnamedDefaultSessionName] = GetSessionStoragePath(UnnamedDefaultSessionName);
                    _activeSessionName = UnnamedDefaultSessionName;
                }
                else
                {
                    // No existing containers anywhere - create the default named session
                    _sessionSettings[DefaultActiveSessionName] = GetSessionStoragePath(DefaultActiveSessionName);
                    _activeSessionName = DefaultActiveSessionName;
                }
            }

            EnsureSessionInitialized(_activeSessionName);
            SaveRegistryLocked();
        }
    }

    private bool HasDefaultUnnamedSessionOnFirstRun()
    {
        try
        {
            // Try to list containers in the unnamed default session
            string json = RunProcessCommandAsync("wslc", "container list --all --format json", CancellationToken.None).GetAwaiter().GetResult();
            var containers = JsonSerializer.Deserialize<List<ContainerListItem>>(json, JsonOptions);
            return containers is { Count: > 0 };
        }
        catch
        {
            return false;
        }
    }

    private SessionRegistry? TryLoadRegistryLocked()
    {
        if (!File.Exists(_sessionRegistryPath))
        {
            return null;
        }

        try
        {
            string json = File.ReadAllText(_sessionRegistryPath);
            return JsonSerializer.Deserialize<SessionRegistry>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WslcBackendService] Failed to load session registry from '{_sessionRegistryPath}': {ex.Message}");
            return null;
        }
    }

    private void SaveRegistryLocked()
    {
        try
        {
            string? registryDir = Path.GetDirectoryName(_sessionRegistryPath);
            if (registryDir is not null)
            {
                Directory.CreateDirectory(registryDir);
            }

            var registry = new SessionRegistry(
                _activeSessionName,
                _sessionSettings.Keys.Where(n => !string.IsNullOrWhiteSpace(n)).ToArray());
            string json = JsonSerializer.Serialize(registry, JsonOptions);
            File.WriteAllText(_sessionRegistryPath, json);
        }
        catch (Exception ex)
        {
            // Best-effort persistence; don't crash the service if the file cannot be written.
            System.Diagnostics.Debug.WriteLine($"[WslcBackendService] Failed to save session registry to '{_sessionRegistryPath}': {ex.Message}");
        }
    }

    private static void StartSessionIfNeeded(Session session)
    {
        try
        {
            session.Start();
        }
        catch (COMException ex) when (ex.HResult == HrFileExists)
        {
            // The session storage and backing artifacts already exist. Treat this as
            // attach-to-existing-session and keep using the Session instance.
        }
    }

    private IEnumerable<string> GetKnownSessionNamesLocked()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string name in _sessionSettings.Keys)
        {
            names.Add(name);
        }

        // Always include the active session name so it is visible even before
        // EnsureSessionInitialized has added it to _sessionSettings.
        names.Add(_activeSessionName);
        return names;
    }

    private string GetSessionStoragePath(string sessionName)
    {
        if (sessionName.Equals(UnnamedDefaultSessionName, StringComparison.OrdinalIgnoreCase))
        {
            // Default unnamed session has no local storage directory
            return "(default)";
        }
        return Path.Combine(_baseStoragePath, sessionName);
    }

    private string GetFallbackActiveSessionNameLocked(string removedSessionName)
    {
        string? otherNamedSession = _sessionSettings.Keys
            .Concat(_sessions.Keys)
            .Where(name => !string.IsNullOrWhiteSpace(name)
                && !string.Equals(name, removedSessionName, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(name, UnnamedDefaultSessionName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(otherNamedSession))
        {
            return otherNamedSession;
        }

        bool hasDefaultSession = _sessionSettings.ContainsKey(UnnamedDefaultSessionName)
            || _sessions.ContainsKey(UnnamedDefaultSessionName);
        if (hasDefaultSession)
        {
            return UnnamedDefaultSessionName;
        }

        return DefaultActiveSessionName;
    }

    public DateTimeOffset GetActiveSessionCreatedAtUtc()
    {
        string sessionName = GetActiveSessionNameSnapshot();
        string sessionPath = GetSessionStoragePath(sessionName);
        return GetPathCreatedAtUtc(sessionPath);
    }

    private async Task<IReadOnlyList<ContainerListItem>> GetContainersAsync(CancellationToken cancellationToken)
    {
        string json = await RunWslcActiveSessionCommandAsync("list --all --format json", cancellationToken);
        return JsonSerializer.Deserialize<List<ContainerListItem>>(json, JsonOptions) ?? [];
    }

    private async Task<IReadOnlyList<ContainerStatsItem>> GetContainerStatsAsync(CancellationToken cancellationToken)
    {
        string json = await RunWslcActiveSessionCommandAsync("stats --format json", cancellationToken);
        return JsonSerializer.Deserialize<List<ContainerStatsItem>>(json, JsonOptions) ?? [];
    }

    private static async Task<string> RunWslcCommandAsync(string arguments, CancellationToken cancellationToken)
    {
        return await RunProcessCommandAsync("wslc", arguments, cancellationToken);
    }

    private Task<string> RunWslcActiveSessionCommandAsync(string arguments, CancellationToken cancellationToken)
    {
        return RunWslcCommandAsync(BuildSessionScopedWslcArguments(GetActiveSessionNameSnapshot(), arguments), cancellationToken);
    }

    private string GetActiveSessionNameSnapshot()
    {
        lock (_syncLock)
        {
            return _activeSessionName;
        }
    }

    private static string BuildSessionScopedWslcArguments(string sessionName, string arguments)
    {
        if (string.IsNullOrWhiteSpace(sessionName) || sessionName.Equals(UnnamedDefaultSessionName, StringComparison.OrdinalIgnoreCase))
        {
            // Use unnamed default session (no --session parameter)
            return arguments;
        }

        return $"--session {EscapeCliArgument(sessionName.Trim())} {arguments}";
    }

    private static string GetImageFromInspect(string inspectJson)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(inspectJson);
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("Image", out JsonElement imageElement))
            {
                return imageElement.GetString() ?? "alpine:latest";
            }
        }
        catch
        {
        }

        return "alpine:latest";
    }

    private static string BuildContainerLogsArguments(
        string containerReference,
        string? tail,
        bool timestamps,
        string? since,
        string? until,
        bool follow)
    {
        var arguments = new StringBuilder();
        arguments.Append("logs ");

        if (follow)
        {
            arguments.Append("--follow ");
        }

        if (!string.IsNullOrWhiteSpace(tail) && !string.Equals(tail, "all", StringComparison.OrdinalIgnoreCase))
        {
            arguments.Append("--tail ");
            arguments.Append(EscapeCliArgument(tail.Trim()));
            arguments.Append(' ');
        }

        if (timestamps)
        {
            arguments.Append("--timestamps ");
        }

        if (!string.IsNullOrWhiteSpace(since))
        {
            arguments.Append("--since ");
            arguments.Append(EscapeCliArgument(since.Trim()));
            arguments.Append(' ');
        }

        if (!string.IsNullOrWhiteSpace(until))
        {
            arguments.Append("--until ");
            arguments.Append(EscapeCliArgument(until.Trim()));
            arguments.Append(' ');
        }

        arguments.Append(EscapeCliArgument(containerReference.Trim()));
        return arguments.ToString();
    }

    private static async Task<string> RunProcessCommandAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        using System.Diagnostics.Process process = StartProcessCommand(fileName, arguments);

        Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        await Task.WhenAll(standardOutputTask, standardErrorTask);

        string standardOutput = await standardOutputTask;
        string standardError = await standardErrorTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(standardError)
                ? $"{fileName} {arguments} failed with exit code {process.ExitCode}."
                : standardError.Trim());
        }

        return standardOutput;
    }

    private static System.Diagnostics.Process StartProcessCommand(string fileName, string arguments)
    {
        return System.Diagnostics.Process.Start(new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException($"Failed to start {fileName}.");
    }

    private static void TryTerminateProcess(System.Diagnostics.Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }

    private sealed record StoredDevContainerRecord(
        string Id,
        string? Name,
        string InspectJson,
        DateTimeOffset UpdatedAtUtc);

    public sealed record DockerImageDetails(
        string Id,
        string Repository,
        string Tag,
        string Reference,
        DateTimeOffset CreatedAt,
        ulong Size);

    private static string FormatCpuUsage(IReadOnlyList<ContainerStatsItem> stats)
    {
        if (stats.Count == 0)
        {
            return "Idle";
        }

        double totalPercent = stats.Sum(stat => ParsePercentage(stat.CPUPerc));
        return $"{totalPercent:0.##}%";
    }

    private static double ComputeCpuPercent(IReadOnlyList<ContainerStatsItem> stats) =>
        stats.Sum(stat => ParsePercentage(stat.CPUPerc));

    private static double ComputeMemoryPercent(IReadOnlyList<ContainerStatsItem> stats)
    {
        ulong usedBytes = 0;
        ulong totalBytes = 0;
        foreach (ContainerStatsItem stat in stats)
        {
            ParseMemoryUsage(stat.MemUsage, out ulong used, out ulong total);
            usedBytes += used;
            totalBytes = Math.Max(totalBytes, total);
        }

        return totalBytes == 0 ? 0 : Math.Clamp((double)usedBytes / totalBytes * 100.0, 0, 100);
    }

    private static string FormatMemoryUsage(IReadOnlyList<ContainerStatsItem> stats)
    {
        if (stats.Count == 0)
        {
            return "Idle";
        }

        ulong usedBytes = 0;
        ulong totalBytes = 0;

        foreach (ContainerStatsItem stat in stats)
        {
            ParseMemoryUsage(stat.MemUsage, out ulong used, out ulong total);
            usedBytes += used;
            totalBytes = Math.Max(totalBytes, total);
        }

        return totalBytes == 0
            ? FormatBytes(usedBytes)
            : $"{FormatBytes(usedBytes)} / {FormatBytes(totalBytes)}";
    }

    private static double ParsePercentage(string percentageText)
    {
        string normalized = percentageText.Trim().TrimEnd('%');
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : 0;
    }

    private static void ParseMemoryUsage(string memoryUsageText, out ulong usedBytes, out ulong totalBytes)
    {
        usedBytes = 0;
        totalBytes = 0;

        string[] parts = memoryUsageText.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0)
        {
            usedBytes = ParseHumanSize(parts[0]);
        }

        if (parts.Length > 1)
        {
            totalBytes = ParseHumanSize(parts[1]);
        }
    }

    private static ulong ParseHumanSize(string value)
    {
        string[] parts = value.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return 0;
        }

        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double amount))
        {
            return 0;
        }

        double multiplier = parts.Length < 2
            ? 1
            : parts[1].ToUpperInvariant() switch
            {
                "B" => 1,
                "KIB" => 1024d,
                "MIB" => 1024d * 1024d,
                "GIB" => 1024d * 1024d * 1024d,
                "TIB" => 1024d * 1024d * 1024d * 1024d,
                "KB" => 1000d,
                "MB" => 1000d * 1000d,
                "GB" => 1000d * 1000d * 1000d,
                "TB" => 1000d * 1000d * 1000d * 1000d,
                _ => 1,
            };

        return (ulong)Math.Round(amount * multiplier, MidpointRounding.AwayFromZero);
    }

    private static string FormatBytes(ulong bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        double size = bytes;
        int unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return $"{size:0.##} {units[unitIndex]}";
    }

    private static bool ImageMatches(ImageInfo image, string imageReference)
    {
        if (string.Equals(image.Name, imageReference, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        byte[] digestBytes = image.Sha256?.ToArray() ?? [];
        if (digestBytes.Length > 0)
        {
            string digest = $"sha256:{Convert.ToHexString(digestBytes).ToLowerInvariant()}";
            if (string.Equals(digest, imageReference, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        SplitReference(image.Name, out string repository, out string tag);
        return string.Equals($"{repository}:{tag}", imageReference, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ImageMatches(CliImageListItem image, string imageReference)
    {
        string reference = BuildImageReference(image.Repository, image.Tag);
        if (string.Equals(reference, imageReference, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(image.Id) && string.Equals(image.Id, imageReference, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(image.Repository, imageReference, StringComparison.OrdinalIgnoreCase);
    }

    private static ImageSummary ToImageSummary(ImageInfo image)
    {
        string reference = image.Name;
        SplitReference(reference, out string repository, out string tag);
        byte[] digestBytes = image.Sha256?.ToArray() ?? [];
        string digest = digestBytes.Length == 0
            ? reference
            : $"sha256:{Convert.ToHexString(digestBytes).ToLowerInvariant()}";

        return new ImageSummary(
            digest,
            repository,
            tag,
            ToRelativeText(image.CreatedTimestamp),
            ToSizeLabel(image.Size),
            reference,
            image.CreatedTimestamp,
            ToSizeBytes(image.Size));
    }

    private static ImageSummary ToImageSummary(CliImageListItem image)
    {
        string reference = BuildImageReference(image.Repository, image.Tag);
        string digest = string.IsNullOrWhiteSpace(image.Id) ? reference : image.Id;

        return new ImageSummary(
            digest,
            image.Repository,
            string.IsNullOrWhiteSpace(image.Tag) ? "latest" : image.Tag,
            ToRelativeText(FromUnixTimeSecondsOrNow(image.Created)),
            ToSizeLabel(image.Size),
            reference,
            FromUnixTimeSecondsOrNow(image.Created),
            ToSizeBytes(image.Size));

    }

    private static long ToSizeBytes(ulong size)
    {
        return size > long.MaxValue ? long.MaxValue : (long)size;
    }

    private static string BuildImageReference(string repository, string? tag)
    {
        string normalizedTag = string.IsNullOrWhiteSpace(tag) ? "latest" : tag.Trim();
        return string.IsNullOrWhiteSpace(repository)
            ? normalizedTag
            : $"{repository}:{normalizedTag}";
    }

    private static void SplitReference(string imageReference, out string repository, out string tag)
    {
        repository = imageReference;
        tag = "latest";
        int tagSeparator = imageReference.LastIndexOf(':');

        if (tagSeparator > 0 && tagSeparator > imageReference.LastIndexOf('/'))
        {
            repository = imageReference[..tagSeparator];
            tag = imageReference[(tagSeparator + 1)..];
        }
    }

    private static string ToRelativeText(DateTimeOffset timestamp)
    {
        var age = DateTimeOffset.UtcNow - timestamp.ToUniversalTime();
        if (age.TotalMinutes < 1)
        {
            return "just now";
        }

        if (age.TotalHours < 1)
        {
            return $"{Math.Max(1, (int)age.TotalMinutes)} minutes ago";
        }

        if (age.TotalDays < 1)
        {
            return $"{Math.Max(1, (int)age.TotalHours)} hours ago";
        }

        return $"{Math.Max(1, (int)age.TotalDays)} days ago";
    }

    private static string ToSizeLabel(ulong bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        int unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return $"{size:0.#} {units[unitIndex]}";
    }

    private static string ToContainerStateText(int state)
    {
        return state switch
        {
            0 => "Created",
            1 => "Starting",
            2 => "Running",
            3 => "Paused",
            4 => "Restarting",
            5 => "Stopping",
            6 => "Stopped",
            7 => "Dead",
            _ => "Unknown",
        };
    }

    private static string EscapeCliArgument(string value)
    {
        var escaped = new System.Text.StringBuilder(value.Length + 2);
        escaped.Append('"');

        int backslashCount = 0;
        foreach (char ch in value)
        {
            if (ch == '\\')
            {
                backslashCount++;
                continue;
            }

            if (ch == '"')
            {
                escaped.Append('\\', backslashCount * 2 + 1);
                escaped.Append('"');
                backslashCount = 0;
                continue;
            }

            if (backslashCount > 0)
            {
                escaped.Append('\\', backslashCount);
                backslashCount = 0;
            }

            escaped.Append(ch);
        }

        if (backslashCount > 0)
        {
            // Trailing backslashes must be doubled before the closing quote.
            escaped.Append('\\', backslashCount * 2);
        }

        escaped.Append('"');
        return escaped.ToString();
    }

    private static IReadOnlyList<string> SplitCommandLineArguments(string commandLine)
    {
        var args = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;
        char quoteChar = '\0';

        foreach (char ch in commandLine)
        {
            if ((ch == '"' || ch == '\'') && (!inQuotes || quoteChar == ch))
            {
                if (!inQuotes)
                {
                    inQuotes = true;
                    quoteChar = ch;
                }
                else
                {
                    inQuotes = false;
                    quoteChar = '\0';
                }

                continue;
            }

            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    args.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(ch);
        }

        if (inQuotes)
        {
            throw new InvalidOperationException("Startup command contains an unmatched quote.");
        }

        if (current.Length > 0)
        {
            args.Add(current.ToString());
        }

        return args;
    }

    private sealed record ContainerListItem(string Id, string Name, string Image, int State, long CreatedAt);

    private sealed record CliImageListItem(string Id, string Repository, string Tag, long Created, ulong Size);

    public sealed record DockerExecResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed record ContainerStatsItem(string CPUPerc, string MemUsage);

    private sealed record KubectlPodListResponse(List<KubectlPodItem>? Items);

    private sealed record KubectlPodItem(KubectlMetadata? Metadata, KubectlPodStatus? Status, KubectlPodSpec? Spec);

    private sealed record KubectlMetadata(string? Name, string? Namespace);

    private sealed record KubectlPodStatus(string? Phase);

    private sealed record KubectlPodSpec(string? NodeName);

    private sealed record VolumeUsageData(long? Size, int? RefCount);

    private sealed record VolumeListItem(string? Name, string? Driver, string? Mountpoint, VolumeUsageData? UsageData);

    private sealed record SessionRegistry(
        string ActiveSessionName,
        IReadOnlyList<string> KnownSessionNames);

    private static DateTimeOffset FromUnixTimeSecondsOrNow(long unixSeconds)
    {
        return unixSeconds > 0
            ? DateTimeOffset.FromUnixTimeSeconds(unixSeconds)
            : DateTimeOffset.UtcNow;
    }

    private static DateTimeOffset TryParseCreatedAt(JsonElement root, DateTimeOffset fallback)
    {
        if (root.TryGetProperty("Created", out JsonElement createdElement)
            && createdElement.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(createdElement.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset createdAt))
        {
            return createdAt.ToUniversalTime();
        }

        return fallback;
    }

    private static DateTimeOffset? GetVolumeCreatedAtUtc(string? mountPoint, string? hostPath)
    {
        if (!string.IsNullOrWhiteSpace(mountPoint))
        {
            return GetPathCreatedAtUtc(mountPoint);
        }

        if (!string.IsNullOrWhiteSpace(hostPath))
        {
            return GetPathCreatedAtUtc(hostPath);
        }

        return null;
    }

    private static DateTimeOffset GetPathCreatedAtUtc(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                return Directory.GetCreationTimeUtc(path);
            }

            if (File.Exists(path))
            {
                return File.GetCreationTimeUtc(path);
            }
        }
        catch
        {
        }

        return DateTimeOffset.UtcNow;
    }
}