using Porthole.Core.Models;

namespace Porthole.Core.Services.NamedPipe;

public sealed class NamedPipeNetworkingService : INetworkingService
{
    public async Task<NetworkingSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var response = await NamedPipeImageCatalogService.SendRequestAsync(
            new ImageCatalogRequest(ImageCatalogOperation.GetNetworkingSnapshot),
            progress: null,
            cancellationToken);

        return response.NetworkingSnapshot
            ?? new NetworkingSnapshot(NetworkMode.Bridge, [], new ProxyConfiguration(null, null, null));
    }

    public async Task SetNetworkModeAsync(NetworkMode mode, CancellationToken cancellationToken = default)
    {
        await NamedPipeImageCatalogService.SendRequestAsync(
            new ImageCatalogRequest(ImageCatalogOperation.SetNetworkMode, NetworkMode: mode),
            progress: null,
            cancellationToken);
    }
}
