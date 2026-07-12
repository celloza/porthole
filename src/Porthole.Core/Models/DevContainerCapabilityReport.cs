namespace Porthole.Core.Models;

public enum DevContainerDiagnosticSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
}

public sealed record DevContainerDiagnostic(
    string Code,
    DevContainerDiagnosticSeverity Severity,
    string Message,
    string? JsonPath = null);

public sealed record DevContainerCapabilityReport(
    bool IsSupported,
    IReadOnlyList<DevContainerDiagnostic> Diagnostics);
