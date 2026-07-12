using Porthole.Core.Models;

namespace Porthole.Tray.Services;

internal interface IDockerApiBackend
{
    Task<IReadOnlyList<ContainerSummary>> ListContainersAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ImageSummary>> ListImagesAsync(CancellationToken cancellationToken = default);

    Task<WslcBackendService.DockerImageDetails> GetImageDetailsAsync(string imageReference, CancellationToken cancellationToken = default);

    Task<NetworkingSnapshot> GetNetworkingSnapshotAsync(CancellationToken cancellationToken = default);

    DateTimeOffset GetActiveSessionCreatedAtUtc();

    Task<IReadOnlyList<VolumeSummary>> ListVolumesAsync(CancellationToken cancellationToken = default);

    Task<string> InspectContainerJsonAsync(string containerReference, CancellationToken cancellationToken = default);

    Task<string> GetContainerLogsAsync(string containerReference, string? tail, bool timestamps, string? since, string? until, CancellationToken cancellationToken = default);

    Task<string> CreateDockerContainerAsync(string image, string? name, IReadOnlyList<string>? command, IReadOnlyList<string>? environment, IReadOnlyList<string>? binds, IReadOnlyList<string>? portMappings, CancellationToken cancellationToken = default);

    Task StartContainerAsync(string containerReference, CancellationToken cancellationToken = default);

    Task StopContainerAsync(string containerReference, CancellationToken cancellationToken = default);

    Task RemoveContainerAsync(string containerReference, CancellationToken cancellationToken = default);

    Task<WslcBackendService.DockerExecResult> ExecContainerAsync(string containerReference, IReadOnlyList<string> command, string? workingDirectory, IReadOnlyList<string>? environment, CancellationToken cancellationToken = default);

    Task StreamContainerLogsAsync(string containerReference, Stream destination, string? tail, bool timestamps, string? since, string? until, bool follow, CancellationToken cancellationToken = default);
}