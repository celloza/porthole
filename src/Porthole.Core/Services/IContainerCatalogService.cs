using Porthole.Core.Models;

namespace Porthole.Core.Services;

public interface IContainerCatalogService
{
    Task<IReadOnlyList<ContainerSummary>> ListContainersAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PodSummary>> ListPodsAsync(CancellationToken cancellationToken = default);

    Task SubscribeAsync(
        Func<IReadOnlyList<ContainerSummary>, IReadOnlyList<PodSummary>, Task> onUpdate,
        CancellationToken cancellationToken = default);

    Task StartContainerAsync(ContainerSummary container, CancellationToken cancellationToken = default);

    Task StopContainerAsync(ContainerSummary container, CancellationToken cancellationToken = default);

    Task RemoveContainerAsync(ContainerSummary container, CancellationToken cancellationToken = default);
}