namespace Porthole.Core.Models;

public enum NetworkMode
{
    Bridge,
    Consomme,
}

public sealed record PortBinding(
    string ContainerId,
    string ContainerName,
    int HostPort,
    int ContainerPort,
    string Protocol);

public sealed record ProxyConfiguration(
    string? HttpProxy,
    string? HttpsProxy,
    string? NoProxy);

public sealed record NetworkingSnapshot(
    NetworkMode ActiveMode,
    IReadOnlyList<PortBinding> PortBindings,
    ProxyConfiguration HostProxy);
