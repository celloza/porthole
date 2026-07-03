using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Porthole.Core.Models;

namespace Porthole.Core.Services.NamedPipe;

public sealed class NamedPipeDashboardSnapshotService
{
    private static readonly TimeSpan SnapshotTimeout = TimeSpan.FromSeconds(15);
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<DashboardSnapshot> GetDashboardSnapshotAsync(CancellationToken cancellationToken = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var responseTask = NamedPipeImageCatalogService.SendRequestAsync(
            new ImageCatalogRequest(ImageCatalogOperation.DashboardSnapshot),
            null,
            timeoutCts.Token);

        Task completedTask = await Task.WhenAny(responseTask, Task.Delay(SnapshotTimeout, cancellationToken));
        if (completedTask != responseTask)
        {
            timeoutCts.Cancel();
            throw new TimeoutException("The dashboard snapshot request timed out.");
        }

        ImageCatalogResponse response = await responseTask;

        return response.Snapshot
            ?? throw new IOException("The dashboard snapshot pipe response did not include a snapshot payload.");
    }

    public async Task SubscribeAsync(Func<DashboardSnapshot, Task> onSnapshot, CancellationToken cancellationToken = default)
    {
        await using var pipe = new NamedPipeClientStream(".", NamedPipeImageCatalogService.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(TimeSpan.FromSeconds(3), cancellationToken);

        using var writer = new StreamWriter(pipe, Utf8NoBom, leaveOpen: true);
        using var reader = new StreamReader(pipe, Utf8NoBom, detectEncodingFromByteOrderMarks: false, leaveOpen: true);

        var requestEnvelope = new ImageCatalogResponse(
            ImageCatalogMessageKind.Request,
            Message: JsonSerializer.Serialize(new ImageCatalogRequest(ImageCatalogOperation.SubscribeDashboard), JsonOptions));

        await writer.WriteLineAsync(JsonSerializer.Serialize(requestEnvelope, JsonOptions));
        await writer.FlushAsync(cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            string? line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                throw new IOException("The dashboard subscription pipe closed unexpectedly.");
            }

            var response = JsonSerializer.Deserialize<ImageCatalogResponse>(NamedPipeImageCatalogService.NormalizePipeJson(line), JsonOptions)
                ?? throw new IOException("The dashboard subscription pipe returned invalid data.");

            switch (response.Kind)
            {
                case ImageCatalogMessageKind.Response when response.Snapshot is not null:
                    await onSnapshot(response.Snapshot);
                    break;
                case ImageCatalogMessageKind.Error:
                    throw new InvalidOperationException(response.Message ?? "The dashboard subscription failed.");
            }
        }
    }
}
