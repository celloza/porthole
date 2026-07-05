using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Porthole.Core.Models;

namespace Porthole.Core.Services.NamedPipe;

public sealed class NamedPipeImageCatalogService : IImageCatalogService
{
    public const string PipeName = "Porthole.Images.v2";
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan IdleResponseTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PullResponseTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan CreateContainerResponseTimeout = TimeSpan.FromMinutes(2);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public async Task<IReadOnlyList<ImageSummary>> ListImagesAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync(new ImageCatalogRequest(ImageCatalogOperation.List), null, cancellationToken);
        return response.Images ?? [];
    }

    public async Task SubscribeAsync(Func<IReadOnlyList<ImageSummary>, Task> onUpdate, CancellationToken cancellationToken = default)
    {
        await using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(TimeSpan.FromSeconds(3), cancellationToken);

        using var writer = new StreamWriter(pipe, Utf8NoBom, leaveOpen: true);
        using var reader = new StreamReader(pipe, Utf8NoBom, detectEncodingFromByteOrderMarks: false, leaveOpen: true);

        var requestEnvelope = new ImageCatalogResponse(
            ImageCatalogMessageKind.Request,
            Message: JsonSerializer.Serialize(new ImageCatalogRequest(ImageCatalogOperation.SubscribeImages), JsonOptions));

        await writer.WriteLineAsync(JsonSerializer.Serialize(requestEnvelope, JsonOptions));
        await writer.FlushAsync(cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            string? line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                throw new IOException("The image subscription pipe closed unexpectedly.");
            }

            var response = JsonSerializer.Deserialize<ImageCatalogResponse>(NormalizePipeJson(line), JsonOptions)
                ?? throw new IOException("The image subscription pipe returned invalid data.");

            switch (response.Kind)
            {
                case ImageCatalogMessageKind.Response:
                    await onUpdate(response.Images ?? []);
                    break;
                case ImageCatalogMessageKind.Error:
                    throw new InvalidOperationException(response.Message ?? "The image subscription failed.");
            }
        }
    }

    public async Task PullImageAsync(string imageReference, IProgress<ImagePullProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        await SendRequestAsync(new ImageCatalogRequest(ImageCatalogOperation.Pull, ImageReference: imageReference), progress, cancellationToken);
    }

    public async Task PruneImagesAsync(CancellationToken cancellationToken = default)
    {
        await SendRequestAsync(new ImageCatalogRequest(ImageCatalogOperation.Prune), null, cancellationToken);
    }

    public async Task TagImageAsync(ImageSummary image, string newTag, CancellationToken cancellationToken = default)
    {
        await SendRequestAsync(new ImageCatalogRequest(ImageCatalogOperation.Tag, ImageReference: image.Reference, NewTag: newTag), null, cancellationToken);
    }

    public async Task DeleteImageAsync(ImageSummary image, CancellationToken cancellationToken = default)
    {
        await SendRequestAsync(new ImageCatalogRequest(ImageCatalogOperation.Delete, ImageReference: image.Reference), null, cancellationToken);
    }

    internal static async Task<ImageCatalogResponse> SendRequestAsync(ImageCatalogRequest request, IProgress<ImagePullProgress>? progress, CancellationToken cancellationToken)
    {
        Exception? lastException = null;

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                return await SendRequestOnceAsync(request, progress, cancellationToken);
            }
            catch (Exception ex) when (attempt < 3 && (ex is IOException || ex is TimeoutException || ex is InvalidOperationException))
            {
                lastException = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(150 * attempt), cancellationToken);
            }
        }

        throw lastException ?? new IOException("The image catalog pipe request failed after retries.");
    }

    private static async Task<ImageCatalogResponse> SendRequestOnceAsync(ImageCatalogRequest request, IProgress<ImagePullProgress>? progress, CancellationToken cancellationToken)
    {
        await using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await WaitAsync(pipe.ConnectAsync(ConnectTimeout, cancellationToken), ConnectTimeout, cancellationToken, "The image catalog pipe connection timed out.");

        using var writer = new StreamWriter(pipe, Utf8NoBom, leaveOpen: true);
        using var reader = new StreamReader(pipe, Utf8NoBom, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        TimeSpan responseTimeout = request.Operation switch
        {
            ImageCatalogOperation.Pull => PullResponseTimeout,
            ImageCatalogOperation.CreateContainer => CreateContainerResponseTimeout,
            _ => IdleResponseTimeout,
        };

        await WaitAsync(
            writer.WriteLineAsync(JsonSerializer.Serialize(new ImageCatalogResponse(ImageCatalogMessageKind.Request, Message: JsonSerializer.Serialize(request, JsonOptions)), JsonOptions)),
            responseTimeout,
            cancellationToken,
            "The image catalog pipe request could not be sent.");
        await WaitAsync(
            writer.FlushAsync(cancellationToken),
            responseTimeout,
            cancellationToken,
            "The image catalog pipe request flush timed out.");

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? line = await ReadLineWithTimeoutAsync(reader, responseTimeout, cancellationToken);
            if (line is null)
            {
                throw new IOException("The image catalog pipe closed before a response was completed.");
            }

            string normalizedLine = NormalizePipeJson(line);
            var response = JsonSerializer.Deserialize<ImageCatalogResponse>(normalizedLine, JsonOptions)
                ?? throw new IOException("The image catalog pipe returned an invalid response.");

            switch (response.Kind)
            {
                case ImageCatalogMessageKind.Progress when response.Progress is not null:
                    progress?.Report(response.Progress);
                    break;
                case ImageCatalogMessageKind.Error:
                    throw new InvalidOperationException(response.Message ?? "The image catalog operation failed.");
                case ImageCatalogMessageKind.Response:
                    return response;
            }
        }
    }

    private static async Task WaitAsync(Task task, TimeSpan timeout, CancellationToken cancellationToken, string timeoutMessage)
    {
        Task completedTask = await Task.WhenAny(task, Task.Delay(timeout, cancellationToken));
        if (completedTask != task)
        {
            throw new TimeoutException(timeoutMessage);
        }

        await task;
    }

    private static async Task<string?> ReadLineWithTimeoutAsync(StreamReader reader, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Task<string?> readTask = reader.ReadLineAsync(cancellationToken).AsTask();
        Task completedTask = await Task.WhenAny(readTask, Task.Delay(timeout, cancellationToken));
        if (completedTask != readTask)
        {
            throw new TimeoutException("The image catalog pipe response timed out.");
        }

        return await readTask;
    }

    internal static string NormalizePipeJson(string line)
    {
        int nullIndex = line.IndexOf('\0');
        if (nullIndex >= 0)
        {
            line = line[..nullIndex];
        }

        return line.Trim().TrimStart('\uFEFF');
    }
}