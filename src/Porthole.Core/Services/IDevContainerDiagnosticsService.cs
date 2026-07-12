using Porthole.Core.Models;

namespace Porthole.Core.Services;

public interface IDevContainerDiagnosticsService
{
    Task<DevContainerCapabilityReport> AnalyzeAsync(string devContainerConfigJson, CancellationToken cancellationToken = default);
}
