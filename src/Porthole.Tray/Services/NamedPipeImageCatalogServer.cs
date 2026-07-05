using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Porthole.Core.Models;
using Porthole.Core.Services.NamedPipe;

namespace Porthole.Tray.Services;

internal sealed class NamedPipeImageCatalogServer(WslcBackendService backendService) : IDisposable
{
    private const string LogEnabledEnvironmentVariable = "PORTHOLE_TRAY_PIPE_LOG";

    private static readonly string DiagnosticsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Porthole");

    private static readonly string PipeLogPath = Path.Combine(DiagnosticsDirectory, "tray-pipe.log");

    private static readonly TimeSpan DashboardTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ListTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly CancellationTokenSource _shutdown = new();
    private Task? _serverLoop;

    public void Start()
    {
        Log("Pipe server start requested.");
        _serverLoop ??= Task.Run(() => RunAsync(_shutdown.Token));
    }

    public void Dispose()
    {
        Log("Pipe server dispose requested.");
        _shutdown.Cancel();
        try
        {
            _serverLoop?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;

            try
            {
                server = new NamedPipeServerStream(
                    NamedPipeImageCatalogService.PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                Log("Waiting for pipe client connection.");
                await server.WaitForConnectionAsync(cancellationToken);
                Log("Pipe client connected.");
                Log("Dispatching pipe request handler.");
                _ = HandleClientAsync(server, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                server?.Dispose();
                Log("Pipe server loop canceled.");
                return;
            }
            catch (Exception ex)
            {
                Log($"Pipe server accept loop error: {ex}");
                server?.Dispose();
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
    {
        Log("Pipe request handler started.");
        try
        {
            await using var ownedServer = server;
            using var reader = new StreamReader(server, Utf8NoBom, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            using var writer = new StreamWriter(server, Utf8NoBom, leaveOpen: true);
            using var writeGate = new SemaphoreSlim(1, 1);

            async Task WriteResponseSafeAsync(ImageCatalogResponse response)
            {
                await writeGate.WaitAsync(cancellationToken);
                try
                {
                    await WriteResponseAsync(writer, response);
                }
                finally
                {
                    writeGate.Release();
                }
            }

            string? line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                Log("Pipe request handler received null line; closing connection.");
                return;
            }

            try
            {
                string normalizedLine = NormalizePipeJson(line);

                var envelope = JsonSerializer.Deserialize<ImageCatalogResponse>(normalizedLine, JsonOptions)
                    ?? throw new IOException("Request payload was empty.");
                if (envelope.Kind != ImageCatalogMessageKind.Request || string.IsNullOrWhiteSpace(envelope.Message))
                {
                    throw new IOException("Unexpected pipe request payload.");
                }

                var request = JsonSerializer.Deserialize<ImageCatalogRequest>(NormalizePipeJson(envelope.Message), JsonOptions)
                    ?? throw new IOException("Request payload could not be parsed.");

                Log($"Pipe request operation received: {request.Operation}");

                switch (request.Operation)
                {
                    case ImageCatalogOperation.SubscribeDashboard:
                        await HandleDashboardSubscriptionAsync(writer, cancellationToken);
                        break;
                    case ImageCatalogOperation.SubscribeImages:
                        await HandleImageSubscriptionAsync(writer, cancellationToken);
                        break;
                    case ImageCatalogOperation.SubscribeContainers:
                        await HandleContainerSubscriptionAsync(writer, cancellationToken);
                        break;
                    case ImageCatalogOperation.DashboardSnapshot:
                        await WriteResponseSafeAsync(
                            new ImageCatalogResponse(
                                ImageCatalogMessageKind.Response,
                                Snapshot: await ExecuteWithTimeoutAsync(
                                    token => backendService.GetDashboardSnapshotAsync(token),
                                    DashboardTimeout,
                                    cancellationToken,
                                    "Dashboard snapshot request timed out.")));
                        Log("Dashboard snapshot response sent.");
                        break;
                    case ImageCatalogOperation.List:
                        await WriteResponseSafeAsync(
                            new ImageCatalogResponse(
                                ImageCatalogMessageKind.Response,
                                Images: await ExecuteWithTimeoutAsync(
                                    token => backendService.ListImagesAsync(token),
                                    ListTimeout,
                                    cancellationToken,
                                    "Image list request timed out.")));
                        Log("Image list response sent.");
                        break;
                    case ImageCatalogOperation.ListContainers:
                        await WriteResponseSafeAsync(
                            new ImageCatalogResponse(
                                ImageCatalogMessageKind.Response,
                                Containers: await ExecuteWithTimeoutAsync(
                                    token => backendService.ListContainersAsync(token),
                                    ListTimeout,
                                    cancellationToken,
                                    "Container list request timed out.")));
                        Log("Container list response sent.");
                        break;
                    case ImageCatalogOperation.ListPods:
                        await WriteResponseSafeAsync(
                            new ImageCatalogResponse(
                                ImageCatalogMessageKind.Response,
                                Pods: await ExecuteWithTimeoutAsync(
                                    token => backendService.ListPodsAsync(token),
                                    ListTimeout,
                                    cancellationToken,
                                    "Pod list request timed out.")));
                        Log("Pod list response sent.");
                        break;
                    case ImageCatalogOperation.StartContainer:
                        await backendService.StartContainerAsync(
                            request.ContainerReference ?? throw new IOException("Container reference is required."),
                            cancellationToken);
                        await WriteResponseSafeAsync(new ImageCatalogResponse(ImageCatalogMessageKind.Response, Message: "Container start complete."));
                        Log("Container start response sent.");
                        break;
                    case ImageCatalogOperation.StopContainer:
                        await backendService.StopContainerAsync(
                            request.ContainerReference ?? throw new IOException("Container reference is required."),
                            cancellationToken);
                        await WriteResponseSafeAsync(new ImageCatalogResponse(ImageCatalogMessageKind.Response, Message: "Container stop complete."));
                        Log("Container stop response sent.");
                        break;
                    case ImageCatalogOperation.RemoveContainer:
                        await backendService.RemoveContainerAsync(
                            request.ContainerReference ?? throw new IOException("Container reference is required."),
                            cancellationToken);
                        await WriteResponseSafeAsync(new ImageCatalogResponse(ImageCatalogMessageKind.Response, Message: "Container remove complete."));
                        Log("Container remove response sent.");
                        break;
                    case ImageCatalogOperation.Pull:
                        await backendService.PullImageAsync(
                            request.ImageReference ?? throw new IOException("Image reference is required."),
                            new Progress<Porthole.Core.Models.ImagePullProgress>(update =>
                            {
                                WriteResponseSafeAsync(new ImageCatalogResponse(ImageCatalogMessageKind.Progress, Progress: update)).GetAwaiter().GetResult();
                            }),
                            cancellationToken);
                        await WriteResponseSafeAsync(new ImageCatalogResponse(ImageCatalogMessageKind.Response, Message: "Pull complete."));
                        Log("Image pull response sent.");
                        break;
                    case ImageCatalogOperation.Tag:
                        await backendService.TagImageAsync(
                            request.ImageReference ?? throw new IOException("Image reference is required."),
                            request.NewTag ?? throw new IOException("A new tag is required."),
                            cancellationToken);
                        await WriteResponseSafeAsync(new ImageCatalogResponse(ImageCatalogMessageKind.Response, Message: "Tag complete."));
                        Log("Image tag response sent.");
                        break;
                    case ImageCatalogOperation.Delete:
                        await backendService.DeleteImageAsync(request.ImageReference ?? throw new IOException("Image reference is required."), cancellationToken);
                        await WriteResponseSafeAsync(new ImageCatalogResponse(ImageCatalogMessageKind.Response, Message: "Delete complete."));
                        Log("Image delete response sent.");
                        break;
                    case ImageCatalogOperation.Prune:
                        await backendService.PruneImagesAsync(cancellationToken);
                        await WriteResponseSafeAsync(new ImageCatalogResponse(ImageCatalogMessageKind.Response, Message: "Prune complete."));
                        Log("Image prune response sent.");
                        break;
                    case ImageCatalogOperation.ListSessions:
                        await WriteResponseSafeAsync(
                            new ImageCatalogResponse(
                                ImageCatalogMessageKind.Response,
                                Sessions: backendService.ListSessions()));
                        Log("Session list response sent.");
                        break;
                    case ImageCatalogOperation.CreateSession:
                        backendService.CreateNamedSession(
                            request.SessionName ?? throw new IOException("Session name is required."));
                        await WriteResponseSafeAsync(new ImageCatalogResponse(ImageCatalogMessageKind.Response, Message: "Session created."));
                        Log("Create session response sent.");
                        break;
                    case ImageCatalogOperation.DeleteSession:
                        backendService.DeleteNamedSession(
                            request.SessionName ?? throw new IOException("Session name is required."));
                        await WriteResponseSafeAsync(new ImageCatalogResponse(ImageCatalogMessageKind.Response, Message: "Session deleted."));
                        Log("Delete session response sent.");
                        break;
                    case ImageCatalogOperation.SetActiveSession:
                        backendService.SetActiveSession(
                            request.SessionName ?? throw new IOException("Session name is required."));
                        await WriteResponseSafeAsync(new ImageCatalogResponse(ImageCatalogMessageKind.Response, Message: "Active session updated."));
                        Log("Set active session response sent.");
                        break;
                    case ImageCatalogOperation.GetActiveSession:
                        await WriteResponseSafeAsync(
                            new ImageCatalogResponse(
                                ImageCatalogMessageKind.Response,
                                Message: backendService.GetActiveSessionName()));
                        Log("Get active session response sent.");
                        break;
                    case ImageCatalogOperation.GetNetworkingSnapshot:
                        await WriteResponseSafeAsync(
                            new ImageCatalogResponse(
                                ImageCatalogMessageKind.Response,
                                NetworkingSnapshot: await backendService.GetNetworkingSnapshotAsync(cancellationToken)));
                        Log("Networking snapshot response sent.");
                        break;
                    case ImageCatalogOperation.SetNetworkMode:
                        backendService.SetNetworkMode(
                            request.NetworkMode ?? throw new IOException("Network mode is required."));
                        await WriteResponseSafeAsync(new ImageCatalogResponse(ImageCatalogMessageKind.Response, Message: "Network mode updated."));
                        Log("Set network mode response sent.");
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported image operation: {request.Operation}");
                }
            }
            catch (Exception ex)
            {
                Log($"Pipe request handler error: {ex}");

                try
                {
                    await WriteResponseSafeAsync(new ImageCatalogResponse(ImageCatalogMessageKind.Error, Message: ex.Message));
                    Log("Pipe error response sent.");
                }
                catch (Exception writeEx)
                {
                    Log($"Pipe error response write failed: {writeEx}");
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Pipe request handler fatal error: {ex}");
        }
        finally
        {
            Log("Pipe request handler completed.");
        }
    }

    private static Task WriteResponseAsync(StreamWriter writer, ImageCatalogResponse response)
    {
        return WriteWithTimeoutAsync(writer, JsonSerializer.Serialize(response, JsonOptions));
    }

    private static async Task WriteWithTimeoutAsync(StreamWriter writer, string payload)
    {
        Task writeTask = writer.WriteLineAsync(payload);
        Task completedTask = await Task.WhenAny(writeTask, Task.Delay(WriteTimeout));
        if (completedTask != writeTask)
        {
            throw new TimeoutException("Timed out writing to the named pipe client.");
        }

        await writeTask;

        Task flushTask = writer.FlushAsync();
        completedTask = await Task.WhenAny(flushTask, Task.Delay(WriteTimeout));
        if (completedTask != flushTask)
        {
            throw new TimeoutException("Timed out flushing to the named pipe client.");
        }

        await flushTask;
    }

    private static async Task<T> ExecuteWithTimeoutAsync<T>(Func<CancellationToken, Task<T>> action, TimeSpan timeout, CancellationToken cancellationToken, string timeoutMessage)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<T> actionTask = action(timeoutCts.Token);
        Task completedTask = await Task.WhenAny(actionTask, Task.Delay(timeout, cancellationToken));
        if (completedTask != actionTask)
        {
            timeoutCts.Cancel();
            throw new TimeoutException(timeoutMessage);
        }

        return await actionTask;
    }

    private static void Log(string message)
    {
        if (!IsLoggingEnabled())
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(DiagnosticsDirectory);

            const long maxSizeBytes = 512 * 1024;
            if (File.Exists(PipeLogPath))
            {
                var info = new FileInfo(PipeLogPath);
                if (info.Length > maxSizeBytes)
                {
                    string archivedPath = PipeLogPath + ".1";
                    if (File.Exists(archivedPath))
                    {
                        File.Delete(archivedPath);
                    }

                    File.Move(PipeLogPath, archivedPath);
                }
            }

            File.AppendAllText(PipeLogPath, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    private static bool IsLoggingEnabled()
    {
        string? value = Environment.GetEnvironmentVariable(LogEnabledEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Equals("1", StringComparison.OrdinalIgnoreCase)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || value.Equals("on", StringComparison.OrdinalIgnoreCase);
    }

    private async Task HandleDashboardSubscriptionAsync(StreamWriter writer, CancellationToken cancellationToken)
    {
        Log("Dashboard subscription started.");

        while (!cancellationToken.IsCancellationRequested)
        {
            DashboardSnapshot snapshot = await ExecuteWithTimeoutAsync(
                token => backendService.GetDashboardSnapshotAsync(token),
                DashboardTimeout,
                cancellationToken,
                "Dashboard subscription update timed out.");

            await WriteResponseAsync(writer, new ImageCatalogResponse(ImageCatalogMessageKind.Response, Snapshot: snapshot));
            Log("Dashboard subscription update sent.");

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
    }

    private async Task HandleContainerSubscriptionAsync(StreamWriter writer, CancellationToken cancellationToken)
    {
        Log("Container subscription started.");

        while (!cancellationToken.IsCancellationRequested)
        {
            IReadOnlyList<ContainerSummary> containers = await ExecuteWithTimeoutAsync(
                token => backendService.ListContainersAsync(token),
                ListTimeout,
                cancellationToken,
                "Container subscription update timed out.");

            IReadOnlyList<PodSummary> pods = await ExecuteWithTimeoutAsync(
                token => backendService.ListPodsAsync(token),
                ListTimeout,
                cancellationToken,
                "Pod subscription update timed out.");

            await WriteResponseAsync(writer, new ImageCatalogResponse(
                ImageCatalogMessageKind.Response,
                Containers: containers,
                Pods: pods));
            Log("Container subscription update sent.");

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
    }

    private async Task HandleImageSubscriptionAsync(StreamWriter writer, CancellationToken cancellationToken)
    {
        Log("Image subscription started.");

        while (!cancellationToken.IsCancellationRequested)
        {
            IReadOnlyList<ImageSummary> images = await ExecuteWithTimeoutAsync(
                token => backendService.ListImagesAsync(token),
                ListTimeout,
                cancellationToken,
                "Image subscription update timed out.");

            await WriteResponseAsync(writer, new ImageCatalogResponse(
                ImageCatalogMessageKind.Response,
                Images: images));
            Log("Image subscription update sent.");

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
    }

    private static string NormalizePipeJson(string line)
    {
        int nullIndex = line.IndexOf('\0');
        if (nullIndex >= 0)
        {
            line = line[..nullIndex];
        }

        return line.Trim().TrimStart('\uFEFF');
    }
}