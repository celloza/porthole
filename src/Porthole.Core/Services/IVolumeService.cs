using Porthole.Core.Models;

namespace Porthole.Core.Services;

public interface IVolumeService
{
    Task<IReadOnlyList<VolumeSummary>> ListVolumesAsync(CancellationToken cancellationToken = default);

    Task CreateVolumeAsync(string name, CancellationToken cancellationToken = default);

    Task DeleteVolumeAsync(string name, CancellationToken cancellationToken = default);

    Task PruneVolumesAsync(CancellationToken cancellationToken = default);
}
