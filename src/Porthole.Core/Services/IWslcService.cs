using Porthole.Core.Models;

namespace Porthole.Core.Services;

public interface IWslcService
{
    WslcPrerequisiteReport GetMissingComponents();

    Task<DashboardSnapshot> GetDashboardSnapshotAsync(CancellationToken cancellationToken = default);
}