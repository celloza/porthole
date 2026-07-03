using Porthole.Core.Models;

namespace Porthole.Core.Services;

public sealed class MockImageCatalogService : IImageCatalogService
{
    private readonly List<ImageSummary> _images =
    [
        new("sha256:18cc5802b3f9", "mcr.microsoft.com/cbl-mariner/base/core", "2.0", "2 days ago", "139 MB", "mcr.microsoft.com/cbl-mariner/base/core:2.0"),
        new("sha256:4b7a1d0210cc", "redis", "7.4", "6 hours ago", "117 MB", "redis:7.4"),
        new("sha256:dbf1ec8e2a92", "ghcr.io/devcontainers/features/common-utils", "2", "Just now", "61 MB", "ghcr.io/devcontainers/features/common-utils:2"),
    ];

    public Task<IReadOnlyList<ImageSummary>> ListImagesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<ImageSummary>>(_images.OrderBy(image => image.Repository).ToArray());
    }

    public async Task SubscribeAsync(Func<IReadOnlyList<ImageSummary>, Task> onUpdate, CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await onUpdate(_images.OrderBy(image => image.Repository).ToArray());
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
    }

    public async Task PullImageAsync(string imageReference, IProgress<ImagePullProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        string normalizedReference = imageReference.Trim();
        string repository = normalizedReference;
        string tag = "latest";
        int tagSeparator = normalizedReference.LastIndexOf(':');

        if (tagSeparator > 0 && tagSeparator > normalizedReference.LastIndexOf('/'))
        {
            repository = normalizedReference[..tagSeparator];
            tag = normalizedReference[(tagSeparator + 1)..];
        }

        for (int percent = 5; percent <= 100; percent += 15)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new ImagePullProgress(percent, $"Pulling {normalizedReference}... {percent}%"));
            await Task.Delay(120, cancellationToken);
        }

        _images.RemoveAll(image => string.Equals(image.Repository, repository, StringComparison.OrdinalIgnoreCase)
            && string.Equals(image.Tag, tag, StringComparison.OrdinalIgnoreCase));

        _images.Add(new ImageSummary($"sha256:{Guid.NewGuid():N}"[..19], repository, tag, "Just now", "Pending size", $"{repository}:{tag}"));
    }

    public Task PruneImagesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_images.Count > 2)
        {
            _images.RemoveAt(0);
        }

        return Task.CompletedTask;
    }

    public Task TagImageAsync(ImageSummary image, string newTag, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string normalizedTag = newTag.Trim();
        string repository = normalizedTag;
        string tag = "latest";
        int tagSeparator = normalizedTag.LastIndexOf(':');
        if (tagSeparator > 0 && tagSeparator > normalizedTag.LastIndexOf('/'))
        {
            repository = normalizedTag[..tagSeparator];
            tag = normalizedTag[(tagSeparator + 1)..];
        }

        _images.Add(image with { Repository = repository, Tag = tag, CreatedRelative = "Just now", Reference = normalizedTag });
        return Task.CompletedTask;
    }

    public Task DeleteImageAsync(ImageSummary image, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _images.RemoveAll(candidate => candidate.Id == image.Id && candidate.Tag == image.Tag && candidate.Repository == image.Repository);
        return Task.CompletedTask;
    }
}