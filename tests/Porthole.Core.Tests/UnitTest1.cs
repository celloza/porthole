using Porthole.Core.Models;
using Porthole.Core.Services;
using Porthole.Core.ViewModels;

namespace Porthole.Core.Tests;

public class ViewModelTests
{
    [Fact]
    public async Task ImagesViewModel_Refresh_LoadsInventoryAndStatus()
    {
        var images = new List<ImageSummary>
        {
            new("sha256:111", "alpine", "latest", "just now", "8 MB", "alpine:latest"),
            new("sha256:222", "redis", "7.4", "1 day ago", "117 MB", "redis:7.4"),
        };

        var service = new FakeImageCatalogService(images);
        var viewModel = new ImagesViewModel(service);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.Images.Count);
        Assert.Equal("2 images", viewModel.ImageCountLabel);
        Assert.Equal("Loaded 2 cached image records.", viewModel.ActionStatus);
    }

    [Fact]
    public void ImagesViewModel_ApplyInventoryUpdate_ClearsReconnectStatus_WhenInventoryUnchanged()
    {
        var images = new List<ImageSummary>
        {
            new("sha256:111", "alpine", "latest", "just now", "8 MB", "alpine:latest"),
        };

        var service = new FakeImageCatalogService(images);
        var viewModel = new ImagesViewModel(service);

        viewModel.ApplyInventoryUpdate(images);
        viewModel.ActionStatus = "Image subscription disconnected. Reconnecting...";

        // Same payload should still clear stale reconnect text.
        viewModel.ApplyInventoryUpdate(images);

        Assert.Equal("Live image updates connected.", viewModel.ActionStatus);
    }

    [Fact]
    public void ContainersViewModel_ApplyCatalogUpdate_UpdatesCountsAndPodTree()
    {
        var service = new FakeContainerCatalogService();
        var viewModel = new ContainersViewModel(service);

        var containers = new List<ContainerSummary>
        {
            new("id1", "web", "nginx", 2, "Running"),
            new("id2", "cache", "redis", 6, "Stopped"),
        };

        var pods = new List<PodSummary>
        {
            new("default", "api-123", "Running", "node-a"),
            new("kube-system", "coredns-1", "Running", "node-a"),
        };

        viewModel.ApplyCatalogUpdate(containers, pods);

        Assert.Equal("2 containers", viewModel.ContainerCountLabel);
        Assert.Equal("1 running / 1 stopped", viewModel.RunningCountLabel);
        Assert.Contains("k8s", viewModel.PodTreeText);
        Assert.Contains("default", viewModel.PodTreeText);
        Assert.Equal("Loaded 2 container records.", viewModel.ActionStatus);
        Assert.Equal("Loaded 2 pods.", viewModel.PodsStatus);
    }

    private sealed class FakeImageCatalogService(IReadOnlyList<ImageSummary> images) : IImageCatalogService
    {
        public Task<IReadOnlyList<ImageSummary>> ListImagesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(images);
        }

        public Task SubscribeAsync(Func<IReadOnlyList<ImageSummary>, Task> onUpdate, CancellationToken cancellationToken = default)
        {
            return onUpdate(images);
        }

        public Task PullImageAsync(string imageReference, IProgress<ImagePullProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task PruneImagesAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task TagImageAsync(ImageSummary image, string newTag, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task DeleteImageAsync(ImageSummary image, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeContainerCatalogService : IContainerCatalogService
    {
        public Task<IReadOnlyList<ContainerSummary>> ListContainersAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<ContainerSummary>>([]);
        }

        public Task<IReadOnlyList<PodSummary>> ListPodsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<PodSummary>>([]);
        }

        public Task SubscribeAsync(Func<IReadOnlyList<ContainerSummary>, IReadOnlyList<PodSummary>, Task> onUpdate, CancellationToken cancellationToken = default)
        {
            return onUpdate([], []);
        }

        public Task StartContainerAsync(ContainerSummary container, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task StopContainerAsync(ContainerSummary container, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task RemoveContainerAsync(ContainerSummary container, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
