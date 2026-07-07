using Porthole.Core.Models;

namespace Porthole.Core.Services.NamedPipe;

public sealed class NamedPipeVolumeService : IVolumeService
{
    public async Task<IReadOnlyList<VolumeSummary>> ListVolumesAsync(CancellationToken cancellationToken = default)
    {
        var response = await NamedPipeImageCatalogService.SendRequestAsync(
            new ImageCatalogRequest(ImageCatalogOperation.ListVolumes),
            progress: null,
            cancellationToken);

        return response.Volumes ?? [];
    }

    public async Task CreateVolumeAsync(string name, CancellationToken cancellationToken = default)
    {
        await NamedPipeImageCatalogService.SendRequestAsync(
            new ImageCatalogRequest(ImageCatalogOperation.CreateVolume, VolumeName: name),
            progress: null,
            cancellationToken);
    }

    public async Task DeleteVolumeAsync(string name, CancellationToken cancellationToken = default)
    {
        await NamedPipeImageCatalogService.SendRequestAsync(
            new ImageCatalogRequest(ImageCatalogOperation.DeleteVolume, VolumeName: name),
            progress: null,
            cancellationToken);
    }

    public async Task PruneVolumesAsync(CancellationToken cancellationToken = default)
    {
        await NamedPipeImageCatalogService.SendRequestAsync(
            new ImageCatalogRequest(ImageCatalogOperation.PruneVolumes),
            progress: null,
            cancellationToken);
    }
}
