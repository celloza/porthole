using Porthole.Core.Models;

namespace Porthole.Core.Services;

public interface INetworkingService
{
    Task<NetworkingSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
    Task SetNetworkModeAsync(NetworkMode mode, CancellationToken cancellationToken = default);
}
