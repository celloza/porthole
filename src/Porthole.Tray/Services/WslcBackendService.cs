using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Microsoft.WSL.Containers;
using Porthole.Core.Models;

namespace Porthole.Tray.Services;

internal sealed class WslcBackendService : IDisposable
{
    private const int ContainerStateRunning = 2;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly string BaseStoragePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Porthole",
        "Sessions");

    private readonly object _syncLock = new();
    private Session? _session;

    public SessionSettings CreateDefaultSessionSettings(string sessionName)
    {
        Directory.CreateDirectory(BaseStoragePath);

        string sessionPath = Path.Combine(BaseStoragePath, sessionName);
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
        session.Start();
        return session;
    }

    public Task<IReadOnlyList<ImageSummary>> ListImagesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var session = EnsureSession();

        var images = session
            .GetImages()
            .Select(ToImageSummary)
            .OrderBy(image => image.Repository, StringComparer.OrdinalIgnoreCase)
            .ThenBy(image => image.Tag, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult<IReadOnlyList<ImageSummary>>(images);
    }

    public async Task<IReadOnlyList<ContainerSummary>> ListContainersAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<ContainerListItem> containers = await GetContainersAsync(cancellationToken);

        ContainerSummary[] mapped = containers
            .Select(container => new ContainerSummary(
                container.Id,
                container.Name,
                container.Image,
                container.State,
                ToContainerStateText(container.State)))
            .OrderByDescending(container => container.IsRunning)
            .ThenBy(container => container.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return mapped;
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

        await RunWslcCommandAsync($"start {EscapeCliArgument(containerReference)}", cancellationToken);
    }

    public async Task StopContainerAsync(string containerReference, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(containerReference))
        {
            throw new InvalidOperationException("Container reference is required.");
        }

        await RunWslcCommandAsync($"stop {EscapeCliArgument(containerReference)}", cancellationToken);
    }

    public async Task RemoveContainerAsync(string containerReference, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(containerReference))
        {
            throw new InvalidOperationException("Container reference is required.");
        }

        await RunWslcCommandAsync($"remove {EscapeCliArgument(containerReference)}", cancellationToken);
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
            $"{runningCount} running / {stoppedCount} stopped",
            sessionStatus);
    }

    public async Task PullImageAsync(string imageReference, IProgress<ImagePullProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var session = EnsureSession();
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
        EnsureSession().DeleteImage(imageReference);
        return Task.CompletedTask;
    }

    public Task TagImageAsync(string sourceImageReference, string newTag, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        SplitReference(newTag, out string repository, out string tag);
        EnsureSession().TagImage(new TagImageOptions(sourceImageReference, repository, tag));

        return Task.CompletedTask;
    }

    public Task PruneImagesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotSupportedException("The current Microsoft.WSL.Containers SDK does not expose an image prune API.");
    }

    public void Dispose()
    {
        lock (_syncLock)
        {
            _session?.Terminate();
            _session?.Dispose();
            _session = null;
        }
    }

    private Session EnsureSession()
    {
        lock (_syncLock)
        {
            _session ??= StartSession("Porthole");
            return _session;
        }
    }

    private static async Task<IReadOnlyList<ContainerListItem>> GetContainersAsync(CancellationToken cancellationToken)
    {
        string json = await RunWslcCommandAsync("list --all --format json", cancellationToken);
        return JsonSerializer.Deserialize<List<ContainerListItem>>(json, JsonOptions) ?? [];
    }

    private static async Task<IReadOnlyList<ContainerStatsItem>> GetContainerStatsAsync(CancellationToken cancellationToken)
    {
        string json = await RunWslcCommandAsync("stats --format json", cancellationToken);
        return JsonSerializer.Deserialize<List<ContainerStatsItem>>(json, JsonOptions) ?? [];
    }

    private static async Task<string> RunWslcCommandAsync(string arguments, CancellationToken cancellationToken)
    {
        return await RunProcessCommandAsync("wslc", arguments, cancellationToken);
    }

    private static async Task<string> RunProcessCommandAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        using var process = System.Diagnostics.Process.Start(new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException("Failed to start wslc.");

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

    private static string FormatCpuUsage(IReadOnlyList<ContainerStatsItem> stats)
    {
        if (stats.Count == 0)
        {
            return "Idle";
        }

        double totalPercent = stats.Sum(stat => ParsePercentage(stat.CPUPerc));
        return $"{totalPercent:0.##}%";
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
            reference);
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
        string escaped = value.Replace("\"", "\\\"");
        return $"\"{escaped}\"";
    }

    private sealed record ContainerListItem(string Id, string Name, string Image, int State);

    private sealed record ContainerStatsItem(string CPUPerc, string MemUsage);

    private sealed record KubectlPodListResponse(List<KubectlPodItem>? Items);

    private sealed record KubectlPodItem(KubectlMetadata? Metadata, KubectlPodStatus? Status, KubectlPodSpec? Spec);

    private sealed record KubectlMetadata(string? Name, string? Namespace);

    private sealed record KubectlPodStatus(string? Phase);

    private sealed record KubectlPodSpec(string? NodeName);
}