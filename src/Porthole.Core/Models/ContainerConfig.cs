namespace Porthole.Core.Models;

public sealed record ContainerConfig(
    string Name,
    string ImageReference,
    string? StartupCommand = null,
    IReadOnlyList<string>? PortMappings = null,
    IReadOnlyList<string>? EnvironmentVariables = null,
    IReadOnlyList<string>? VolumeMounts = null);
