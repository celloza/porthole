using Porthole.Core.Models;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Porthole.Core.Services.NamedPipe;

public sealed class NamedPipeContainerCatalogService : IContainerCatalogService
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<IReadOnlyList<ContainerSummary>> ListContainersAsync(CancellationToken cancellationToken = default)
    {
        var response = await NamedPipeImageCatalogService.SendRequestAsync(
            new ImageCatalogRequest(ImageCatalogOperation.ListContainers),
            progress: null,
            cancellationToken);

        return response.Containers ?? [];
    }

    public async Task<IReadOnlyList<PodSummary>> ListPodsAsync(CancellationToken cancellationToken = default)
    {
        var response = await NamedPipeImageCatalogService.SendRequestAsync(
            new ImageCatalogRequest(ImageCatalogOperation.ListPods),
            progress: null,
            cancellationToken);

        return response.Pods ?? [];
    }

    public async Task SubscribeAsync(
        Func<IReadOnlyList<ContainerSummary>, IReadOnlyList<PodSummary>, Task> onUpdate,
        CancellationToken cancellationToken = default)
    {
        await using var pipe = new NamedPipeClientStream(".", NamedPipeImageCatalogService.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(TimeSpan.FromSeconds(3), cancellationToken);

        using var writer = new StreamWriter(pipe, Utf8NoBom, leaveOpen: true);
        using var reader = new StreamReader(pipe, Utf8NoBom, detectEncodingFromByteOrderMarks: false, leaveOpen: true);

        var requestEnvelope = new ImageCatalogResponse(
            ImageCatalogMessageKind.Request,
            Message: JsonSerializer.Serialize(new ImageCatalogRequest(ImageCatalogOperation.SubscribeContainers), JsonOptions));

        await writer.WriteLineAsync(JsonSerializer.Serialize(requestEnvelope, JsonOptions));
        await writer.FlushAsync(cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            string? line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                throw new IOException("The container subscription pipe closed unexpectedly.");
            }

            var response = JsonSerializer.Deserialize<ImageCatalogResponse>(NamedPipeImageCatalogService.NormalizePipeJson(line), JsonOptions)
                ?? throw new IOException("The container subscription pipe returned invalid data.");

            switch (response.Kind)
            {
                case ImageCatalogMessageKind.Response:
                    await onUpdate(response.Containers ?? [], response.Pods ?? []);
                    break;
                case ImageCatalogMessageKind.Error:
                    throw new InvalidOperationException(response.Message ?? "The container subscription failed.");
            }
        }
    }

    public Task StartContainerAsync(ContainerSummary container, CancellationToken cancellationToken = default)
    {
        return SendContainerActionAsync(ImageCatalogOperation.StartContainer, container, cancellationToken);
    }

    public Task StopContainerAsync(ContainerSummary container, CancellationToken cancellationToken = default)
    {
        return SendContainerActionAsync(ImageCatalogOperation.StopContainer, container, cancellationToken);
    }

    public Task RemoveContainerAsync(ContainerSummary container, CancellationToken cancellationToken = default)
    {
        return SendContainerActionAsync(ImageCatalogOperation.RemoveContainer, container, cancellationToken);
    }

    private static async Task SendContainerActionAsync(ImageCatalogOperation operation, ContainerSummary container, CancellationToken cancellationToken)
    {
        string containerReference = string.IsNullOrWhiteSpace(container.Name)
            ? container.Id
            : container.Name;

        await NamedPipeImageCatalogService.SendRequestAsync(
            new ImageCatalogRequest(operation, ContainerReference: containerReference),
            progress: null,
            cancellationToken);
    }
}