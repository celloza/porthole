using Porthole.Core.Models;

namespace Porthole.Core.Services.NamedPipe;

public sealed class NamedPipeDevContainerDiagnosticsService : IDevContainerDiagnosticsService
{
    public async Task<DevContainerCapabilityReport> AnalyzeAsync(string devContainerConfigJson, CancellationToken cancellationToken = default)
    {
        var response = await NamedPipeImageCatalogService.SendRequestAsync(
            new ImageCatalogRequest(
                ImageCatalogOperation.AnalyzeDevContainerConfig,
                DevContainerConfigJson: devContainerConfigJson),
            progress: null,
            cancellationToken);

        return response.DevContainerCapability
            ?? new DevContainerCapabilityReport(
                IsSupported: false,
                [new DevContainerDiagnostic(
                    "DC500",
                    DevContainerDiagnosticSeverity.Error,
                    "No diagnostics payload returned from backend.")]);
    }
}
