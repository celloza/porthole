using Porthole.Core.Models;

namespace Porthole.Core.Services;

public interface IImageCatalogService
{
    Task<IReadOnlyList<ImageSummary>> ListImagesAsync(CancellationToken cancellationToken = default);

    Task SubscribeAsync(Func<IReadOnlyList<ImageSummary>, Task> onUpdate, CancellationToken cancellationToken = default);

    Task PullImageAsync(string imageReference, IProgress<ImagePullProgress>? progress = null, CancellationToken cancellationToken = default);

    Task PruneImagesAsync(CancellationToken cancellationToken = default);

    Task TagImageAsync(ImageSummary image, string newTag, CancellationToken cancellationToken = default);

    Task DeleteImageAsync(ImageSummary image, CancellationToken cancellationToken = default);
}