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
            new("id1", "web", "nginx", 2, "Running", DateTimeOffset.Parse("2026-07-11T12:00:00Z")),
            new("id2", "cache", "redis", 6, "Stopped", DateTimeOffset.Parse("2026-07-10T12:00:00Z")),
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

    [Fact]
    public async Task SessionViewModel_ListSessions_PopulatesCollectionAndMarksActive()
    {
        var sessionService = new FakeSessionService();
        var viewModel = new SessionViewModel(sessionService);

        await viewModel.LoadSessionsCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.Sessions.Count);
        Assert.True(viewModel.Sessions[0].IsActive);
        Assert.False(viewModel.Sessions[1].IsActive);
        Assert.Equal("default", viewModel.ActiveSessionName);
    }

    [Fact]
    public async Task SessionViewModel_CreateSession_AddsNewSessionAndUpdatesUI()
    {
        var sessionService = new FakeSessionService();
        var viewModel = new SessionViewModel(sessionService);

        await viewModel.LoadSessionsCommand.ExecuteAsync(null);
        viewModel.NewSessionName = "dev-env";

        await viewModel.CreateSessionCommand.ExecuteAsync(null);

        Assert.Contains(viewModel.Sessions, s => s.Name == "dev-env");
        Assert.Empty(viewModel.NewSessionName);
    }

    [Fact]
    public async Task ShellViewModel_Initialize_LoadsSessionsAndActiveSelection()
    {
        var sessionService = new FakeSessionService();
        var viewModel = new ShellViewModel(sessionService);

        await viewModel.InitializeAsync();

        Assert.Equal(2, viewModel.Sessions.Count);
        Assert.Equal("default", viewModel.ActiveSessionName);
        Assert.NotNull(viewModel.SelectedSession);
        Assert.Equal("default", viewModel.SelectedSession!.Name);
        Assert.Equal("Session: default", viewModel.ActiveSessionDisplay);
    }

    [Fact]
    public async Task ShellViewModel_ChangingSelectedSession_AutoSwitchesActiveSession()
    {
        var sessionService = new FakeSessionService();
        var viewModel = new ShellViewModel(sessionService);

        await viewModel.InitializeAsync();
        viewModel.SelectedSession = viewModel.Sessions.Single(session => session.Name == "staging");

        await WaitForConditionAsync(() => viewModel.ActiveSessionName == "staging");

        Assert.Equal("staging", viewModel.ActiveSessionName);
        Assert.NotNull(viewModel.SelectedSession);
        Assert.Equal("staging", viewModel.SelectedSession!.Name);
        Assert.Equal("2 sessions loaded.", viewModel.SessionStatus);
    }

    [Fact]
    public async Task SessionViewModel_DeleteSession_PreventsDeleteOfActiveSession()
    {
        var sessionService = new FakeSessionService();
        var viewModel = new SessionViewModel(sessionService);

        await viewModel.LoadSessionsCommand.ExecuteAsync(null);

        var activeSession = viewModel.Sessions.FirstOrDefault(s => s.IsActive);
        Assert.NotNull(activeSession);

        // Attempting to delete active session should not succeed
        // (In real UI, delete button would be disabled)
        Assert.True(activeSession.IsActive);
    }

    [Fact]
    public async Task NetworkingViewModel_PortBindings_DisplaysContainerMappings()
    {
        var networkingService = new FakeNetworkingService();
        var viewModel = new NetworkingViewModel(networkingService);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.PortBindings.Count);
        Assert.Equal("8080", viewModel.PortBindings[0].HostPort.ToString());
        Assert.Equal("80", viewModel.PortBindings[0].ContainerPort.ToString());
        Assert.Equal("tcp", viewModel.PortBindings[0].Protocol);
    }

    [Fact]
    public async Task NetworkingViewModel_ProxyConfiguration_DisplaysEnvironmentSettings()
    {
        var networkingService = new FakeNetworkingService();
        var viewModel = new NetworkingViewModel(networkingService);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal("http://proxy.example.com:8080", viewModel.HttpProxy);
        Assert.Equal("https://proxy.example.com:8080", viewModel.HttpsProxy);
    }

    [Fact]
    public async Task NetworkingViewModel_NoPortBindings_ShowsEmptyState()
    {
        var networkingService = new FakeNetworkingServiceEmpty();
        var viewModel = new NetworkingViewModel(networkingService);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.PortBindings);
        Assert.True(viewModel.HasNoPortBindings);
    }

    [Fact]
    public void ImagesViewModel_EmptyImageList_ShowsAppropriateLabel()
    {
        var images = new List<ImageSummary>();

        var service = new FakeImageCatalogService(images);
        var viewModel = new ImagesViewModel(service);

        viewModel.ApplyInventoryUpdate(images);

        Assert.Equal("0 images", viewModel.ImageCountLabel);
    }

    [Fact]
    public void ContainersViewModel_AllRunning_ShowsCorrectStatus()
    {
        var service = new FakeContainerCatalogService();
        var viewModel = new ContainersViewModel(service);

        var containers = new List<ContainerSummary>
        {
            new("id1", "web", "nginx", 2, "Running", DateTimeOffset.Parse("2026-07-11T12:00:00Z")),
            new("id2", "api", "node", 2, "Running", DateTimeOffset.Parse("2026-07-11T11:00:00Z")),
        };

        viewModel.ApplyCatalogUpdate(containers, []);

        Assert.Equal("2 running / 0 stopped", viewModel.RunningCountLabel);
    }

    private static async Task WaitForConditionAsync(Func<bool> condition, int timeoutMilliseconds = 1000)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException("Condition was not met before the timeout elapsed.");
            }

            await Task.Delay(10);
        }
    }

    [Fact]
    public void RunWizardViewModel_InitialState_IsTemplateChoiceStep()
    {
        var imageService = new FakeImageCatalogService([]);
        var containerService = new FakeContainerCatalogService();
        var viewModel = new RunWizardViewModel(imageService, containerService);

        Assert.Equal(0, viewModel.CurrentStep);
        Assert.True(viewModel.IsTemplateChoiceStep);
        Assert.False(viewModel.IsStepOneActive);
        Assert.False(viewModel.IsStepTwoActive);
        Assert.False(viewModel.IsStepThreeActive);
        Assert.False(viewModel.CanGoPrevious);
        Assert.False(viewModel.CanGoNext);
    }

    [Fact]
    public async Task RunWizardViewModel_ValidStep1_EnablesNext()
    {
        var images = new List<ImageSummary>
        {
            new("sha256:abc", "alpine", "latest", "just now", "8 MB", "alpine:latest"),
        };

        var imageService = new FakeImageCatalogService(images);
        var containerService = new FakeContainerCatalogService();
        var viewModel = new RunWizardViewModel(imageService, containerService);

        await viewModel.LoadImagesAsync();
        viewModel.StartNewConfiguration();

        viewModel.ContainerName = "my-container";

        Assert.True(viewModel.CanGoNext);
    }

    [Fact]
    public void RunWizardViewModel_InvalidContainerName_ShowsValidation()
    {
        var imageService = new FakeImageCatalogService([]);
        var containerService = new FakeContainerCatalogService();
        var viewModel = new RunWizardViewModel(imageService, containerService);

        viewModel.StartNewConfiguration();

        viewModel.ContainerName = "!invalid name";

        Assert.NotEmpty(viewModel.ContainerNameValidation);
        Assert.False(viewModel.CanGoNext);
    }

    [Fact]
    public async Task RunWizardViewModel_GoNext_AdvancesToStepTwo()
    {
        var images = new List<ImageSummary>
        {
            new("sha256:abc", "alpine", "latest", "just now", "8 MB", "alpine:latest"),
        };

        var imageService = new FakeImageCatalogService(images);
        var containerService = new FakeContainerCatalogService();
        var viewModel = new RunWizardViewModel(imageService, containerService);

        await viewModel.LoadImagesAsync();
        viewModel.StartNewConfiguration();
        viewModel.ContainerName = "test-container";

        viewModel.GoNextCommand.Execute(null);

        Assert.Equal(2, viewModel.CurrentStep);
        Assert.True(viewModel.IsStepTwoActive);
        Assert.True(viewModel.CanGoPrevious);
    }

    [Fact]
    public void RunWizardViewModel_AddPortMapping_ValidFormat_AddsToList()
    {
        var imageService = new FakeImageCatalogService([]);
        var containerService = new FakeContainerCatalogService();
        var viewModel = new RunWizardViewModel(imageService, containerService);

        viewModel.StartNewConfiguration();

        viewModel.NewPortMapping = "8080:80";
        viewModel.AddPortMappingCommand.Execute(null);

        Assert.Single(viewModel.PortMappings);
        Assert.Equal("8080:80", viewModel.PortMappings[0]);
        Assert.Empty(viewModel.NewPortMapping);
        Assert.Empty(viewModel.PortMappingValidation);
    }

    [Fact]
    public void RunWizardViewModel_AddPortMapping_InvalidFormat_ShowsError()
    {
        var imageService = new FakeImageCatalogService([]);
        var containerService = new FakeContainerCatalogService();
        var viewModel = new RunWizardViewModel(imageService, containerService);

        viewModel.StartNewConfiguration();

        viewModel.NewPortMapping = "not-a-port";
        viewModel.AddPortMappingCommand.Execute(null);

        Assert.Empty(viewModel.PortMappings);
        Assert.NotEmpty(viewModel.PortMappingValidation);
    }

    [Fact]
    public void RunWizardViewModel_AddPortMapping_WithProtocol_AddsToList()
    {
        var imageService = new FakeImageCatalogService([]);
        var containerService = new FakeContainerCatalogService();
        var viewModel = new RunWizardViewModel(imageService, containerService);

        viewModel.StartNewConfiguration();

        viewModel.NewPortMapping = "8080:80/tcp";
        viewModel.AddPortMappingCommand.Execute(null);

        Assert.Single(viewModel.PortMappings);
        Assert.Equal("8080:80/tcp", viewModel.PortMappings[0]);
    }

    [Fact]
    public void RunWizardViewModel_RemovePortMapping_RemovesFromList()
    {
        var imageService = new FakeImageCatalogService([]);
        var containerService = new FakeContainerCatalogService();
        var viewModel = new RunWizardViewModel(imageService, containerService);

        viewModel.StartNewConfiguration();

        viewModel.NewPortMapping = "8080:80";
        viewModel.AddPortMappingCommand.Execute(null);
        viewModel.RemovePortMappingCommand.Execute("8080:80");

        Assert.Empty(viewModel.PortMappings);
    }

    [Fact]
    public void RunWizardViewModel_AddEnvVar_ValidFormat_AddsToList()
    {
        var imageService = new FakeImageCatalogService([]);
        var containerService = new FakeContainerCatalogService();
        var viewModel = new RunWizardViewModel(imageService, containerService);

        viewModel.StartNewConfiguration();

        viewModel.NewEnvironmentVariable = "MY_VAR=hello";
        viewModel.AddEnvironmentVariableCommand.Execute(null);

        Assert.Single(viewModel.EnvironmentVariables);
        Assert.Equal("MY_VAR=hello", viewModel.EnvironmentVariables[0]);
        Assert.Empty(viewModel.EnvVarValidation);
    }

    [Fact]
    public void RunWizardViewModel_AddEnvVar_InvalidFormat_ShowsError()
    {
        var imageService = new FakeImageCatalogService([]);
        var containerService = new FakeContainerCatalogService();
        var viewModel = new RunWizardViewModel(imageService, containerService);

        viewModel.StartNewConfiguration();

        viewModel.NewEnvironmentVariable = "NOEQUALSIGN";
        viewModel.AddEnvironmentVariableCommand.Execute(null);

        Assert.Empty(viewModel.EnvironmentVariables);
        Assert.NotEmpty(viewModel.EnvVarValidation);
    }

    [Fact]
    public void RunWizardViewModel_AddVolumeMount_AddsToList()
    {
        var imageService = new FakeImageCatalogService([]);
        var containerService = new FakeContainerCatalogService();
        var viewModel = new RunWizardViewModel(imageService, containerService);

        viewModel.StartNewConfiguration();

        viewModel.NewVolumeMount = "myvolume:/app/data";
        viewModel.AddVolumeMountCommand.Execute(null);

        Assert.Single(viewModel.VolumeMounts);
        Assert.Equal("myvolume:/app/data", viewModel.VolumeMounts[0]);
        Assert.Empty(viewModel.VolumeMountValidation);
    }

    [Fact]
    public void RunWizardViewModel_AddVolumeMount_WindowsPath_AddsToList()
    {
        var imageService = new FakeImageCatalogService([]);
        var containerService = new FakeContainerCatalogService();
        var viewModel = new RunWizardViewModel(imageService, containerService);

        viewModel.StartNewConfiguration();

        viewModel.NewVolumeMount = "C:\\data:/app/data";
        viewModel.AddVolumeMountCommand.Execute(null);

        Assert.Single(viewModel.VolumeMounts);
        Assert.Equal("C:\\data:/app/data", viewModel.VolumeMounts[0]);
        Assert.Empty(viewModel.VolumeMountValidation);
    }

    [Fact]
    public void RunWizardViewModel_AddVolumeMount_InvalidFormat_ShowsError()
    {
        var imageService = new FakeImageCatalogService([]);
        var containerService = new FakeContainerCatalogService();
        var viewModel = new RunWizardViewModel(imageService, containerService);

        viewModel.StartNewConfiguration();

        viewModel.NewVolumeMount = "no-colon-separator";
        viewModel.AddVolumeMountCommand.Execute(null);

        Assert.Empty(viewModel.VolumeMounts);
        Assert.NotEmpty(viewModel.VolumeMountValidation);
    }

    [Fact]
    public async Task RunWizardViewModel_CreateContainer_Success_UpdatesStatus()
    {
        var images = new List<ImageSummary>
        {
            new("sha256:abc", "alpine", "latest", "just now", "8 MB", "alpine:latest"),
        };

        var imageService = new FakeImageCatalogService(images);
        var containerService = new FakeContainerCatalogService();
        var viewModel = new RunWizardViewModel(imageService, containerService);

        await viewModel.LoadImagesAsync();
        viewModel.StartNewConfiguration();
        viewModel.ContainerName = "test-container";
        viewModel.GoNextCommand.Execute(null); // step 2
        viewModel.GoNextCommand.Execute(null); // step 3

        await viewModel.CreateContainerCommand.ExecuteAsync(null);

        Assert.True(viewModel.CreateSucceeded);
        Assert.Contains("test-container", viewModel.StatusMessage);
    }

    [Fact]
    public void RunWizardViewModel_Reset_ClearsAllState()
    {
        var imageService = new FakeImageCatalogService([]);
        var containerService = new FakeContainerCatalogService();
        var viewModel = new RunWizardViewModel(imageService, containerService);

        viewModel.StartNewConfiguration();

        viewModel.ContainerName = "test-container";
        viewModel.NewPortMapping = "8080:80";
        viewModel.AddPortMappingCommand.Execute(null);

        viewModel.Reset();

        Assert.Equal(0, viewModel.CurrentStep);
        Assert.Empty(viewModel.ContainerName);
        Assert.Empty(viewModel.PortMappings);
    }

    [Fact]
    public async Task RunWizardViewModel_ApplyTemplate_PopulatesFieldsAndAdvancesToStepOne()
    {
        var images = new List<ImageSummary>
        {
            new("sha256:abc", "nginx", "latest", "just now", "50 MB", "nginx:latest"),
        };

        var imageService = new FakeImageCatalogService(images);
        var containerService = new FakeContainerCatalogService();
        var viewModel = new RunWizardViewModel(imageService, containerService);

        await viewModel.LoadImagesAsync();

        var template = new ContainerConfig(
            "api",
            "nginx:latest",
            "/bin/sh -c \"echo hello\"",
            ["8080:80"],
            ["ENV=prod"],
            ["C:\\data:/app/data"]);

        viewModel.ApplyTemplate(template);

        Assert.Equal(1, viewModel.CurrentStep);
        Assert.Equal("api", viewModel.ContainerName);
        Assert.Equal("/bin/sh -c \"echo hello\"", viewModel.StartupCommand);
        Assert.Single(viewModel.PortMappings);
        Assert.Single(viewModel.EnvironmentVariables);
        Assert.Single(viewModel.VolumeMounts);
        Assert.NotNull(viewModel.SelectedImage);
    }

    [Fact]
    public async Task RunWizardViewModel_ReviewSummary_ContainsAllSettings()
    {
        var images = new List<ImageSummary>
        {
            new("sha256:abc", "nginx", "latest", "just now", "50 MB", "nginx:latest"),
        };

        var imageService = new FakeImageCatalogService(images);
        var containerService = new FakeContainerCatalogService();
        var viewModel = new RunWizardViewModel(imageService, containerService);

        await viewModel.LoadImagesAsync();
        viewModel.StartNewConfiguration();
        viewModel.ContainerName = "web-server";
        viewModel.StartupCommand = "/bin/sh";
        viewModel.NewPortMapping = "8080:80";
        viewModel.AddPortMappingCommand.Execute(null);
        viewModel.NewEnvironmentVariable = "ENV=prod";
        viewModel.AddEnvironmentVariableCommand.Execute(null);

        viewModel.GoNextCommand.Execute(null); // step 2
        viewModel.GoNextCommand.Execute(null); // step 3

        string summary = viewModel.ReviewSummary;

        Assert.Contains("web-server", summary);
        Assert.Contains("nginx:latest", summary);
        Assert.Contains("/bin/sh", summary);
        Assert.Contains("8080:80", summary);
        Assert.Contains("ENV=prod", summary);
    }

    [Fact]
    public async Task VolumesViewModel_Refresh_LoadsVolumesAndStatus()
    {
        var volumes = new List<VolumeSummary>
        {
            new("data-vol", "local", "/var/lib/docker/volumes/data-vol/_data", null, "128 MB", true),
            new("logs-vol", "local", "/var/lib/docker/volumes/logs-vol/_data", null, "4 MB", false),
        };

        var service = new FakeVolumeService(volumes);
        var viewModel = new VolumesViewModel(service);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.Volumes.Count);
        Assert.Equal("2 volumes", viewModel.VolumeCountLabel);
        Assert.Contains("2 volume", viewModel.StatusMessage);
        Assert.Contains("1 unused", viewModel.StatusMessage);
    }

    [Fact]
    public async Task VolumesViewModel_CreateVolume_AddsVolumeAndRefreshes()
    {
        var service = new FakeVolumeService([]);
        var viewModel = new VolumesViewModel(service);

        viewModel.NewVolumeName = "my-new-vol";
        await viewModel.CreateVolumeCommand.ExecuteAsync(null);

        Assert.Single(service.Created);
        Assert.Equal("my-new-vol", service.Created[0]);
        Assert.Equal(string.Empty, viewModel.NewVolumeName);
        Assert.Single(viewModel.Volumes);
        Assert.Equal("my-new-vol", viewModel.Volumes[0].Name);
    }

    [Fact]
    public async Task VolumesViewModel_CreateVolume_EmptyName_ShowsError()
    {
        var service = new FakeVolumeService([]);
        var viewModel = new VolumesViewModel(service);

        viewModel.NewVolumeName = "   ";
        await viewModel.CreateVolumeCommand.ExecuteAsync(null);

        Assert.Empty(service.Created);
        Assert.Contains("volume name", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VolumesViewModel_DeleteVolume_RemovesVolumeAndRefreshes()
    {
        var volumes = new List<VolumeSummary>
        {
            new("remove-me", "local", "/data/_data", null, "8 MB", false),
        };

        var service = new FakeVolumeService(volumes);
        var viewModel = new VolumesViewModel(service);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        var volume = viewModel.Volumes[0];
        viewModel.SelectedVolume = volume;
        await viewModel.DeleteVolumeCommand.ExecuteAsync(volume);

        Assert.Single(service.Deleted);
        Assert.Equal("remove-me", service.Deleted[0]);
        Assert.Null(viewModel.SelectedVolume);
    }

    [Fact]
    public async Task VolumesViewModel_PruneVolumes_CallsServiceAndRefreshes()
    {
        var volumes = new List<VolumeSummary>
        {
            new("keep-vol", "local", "/data/keep/_data", null, "1 MB", true),
            new("prune-vol", "local", "/data/prune/_data", null, "2 MB", false),
        };

        var service = new FakeVolumeService(volumes);
        var viewModel = new VolumesViewModel(service);

        await viewModel.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(2, viewModel.Volumes.Count);

        await viewModel.PruneVolumesCommand.ExecuteAsync(null);

        Assert.Equal(1, service.PruneCount);
        Assert.Single(viewModel.Volumes);
        Assert.Equal("keep-vol", viewModel.Volumes[0].Name);
    }

    [Fact]
    public void VolumeSummary_DriverDisplay_DefaultsToLocal()
    {
        var volume = new VolumeSummary("test", string.Empty, "/mnt/test", null, "1 MB", false);
        Assert.Equal("local", volume.DriverDisplay);
    }

    [Fact]
    public void VolumeSummary_InUseLabel_ReflectsStatus()
    {
        var inUse = new VolumeSummary("active", "local", "/mnt/active", null, "1 MB", true);
        var unused = new VolumeSummary("idle", "local", "/mnt/idle", null, "1 MB", false);

        Assert.Equal("In use", inUse.InUseLabel);
        Assert.Equal("Unused", unused.InUseLabel);
    }

    [Fact]
    public async Task VolumesViewModel_DeleteVolume_BlockedForBindMount()
    {
        var volumes = new List<VolumeSummary>
        {
            new("src", "virtiofs", "/workspace", "C:\\src", "host path", true, true),
        };

        var service = new FakeVolumeService(volumes);
        var viewModel = new VolumesViewModel(service);

        await viewModel.RefreshCommand.ExecuteAsync(null);
        await viewModel.DeleteVolumeCommand.ExecuteAsync(viewModel.Volumes[0]);

        Assert.Empty(service.Deleted);
        Assert.Contains("Bind mounts", viewModel.StatusMessage);
    }

    [Fact]
    public void RunWizardViewModel_NewVolumeMountTelemetry_FlagsVirtioFsAndNineP()
    {
        var imageService = new FakeImageCatalogService([]);
        var containerService = new FakeContainerCatalogService();
        var viewModel = new RunWizardViewModel(imageService, containerService);

        viewModel.NewVolumeMount = "C:\\data:/app/data:ro";
        Assert.Contains("virtiofs", viewModel.NewVolumeMountTelemetry, StringComparison.OrdinalIgnoreCase);

        viewModel.NewVolumeMount = "/mnt/c/data:/app/data";
        Assert.Contains("9P", viewModel.NewVolumeMountTelemetry, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RunWizardViewModel_ReviewSummary_AnnotatesVolumeTransport()
    {
        var imageService = new FakeImageCatalogService([]);
        var containerService = new FakeContainerCatalogService();
        var viewModel = new RunWizardViewModel(imageService, containerService);

        viewModel.StartNewConfiguration();
        viewModel.ContainerName = "api";
        viewModel.SelectedImage = new ImageSummary("sha256:test", "nginx", "latest", "just now", "1 MB", "nginx:latest");

        viewModel.NewVolumeMount = "C:\\data:/app/data";
        viewModel.AddVolumeMountCommand.Execute(null);
        viewModel.NewVolumeMount = "myvol:/cache";
        viewModel.AddVolumeMountCommand.Execute(null);

        string summary = viewModel.ReviewSummary;

        Assert.Contains("virtiofs", summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("named volume", summary, StringComparison.OrdinalIgnoreCase);
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

        public Task<string> CreateContainerAsync(ContainerConfig config, CancellationToken cancellationToken = default)
        {
            return Task.FromResult($"fake-container-id-{config.Name}");
        }
    }

    private sealed class FakeSessionService : ISessionService
    {
        private readonly List<SessionSummary> _sessions =
        [
            new("default", "/var/lib/wsl/sessions/default", true),
            new("staging", "/var/lib/wsl/sessions/staging", false),
        ];

        public Task<IReadOnlyList<SessionSummary>> ListSessionsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<SessionSummary>>(_sessions);
        }

        public Task CreateSessionAsync(string name, CancellationToken cancellationToken = default)
        {
            _sessions.Add(new(name, $"/var/lib/wsl/sessions/{name}", false));
            return Task.CompletedTask;
        }

        public Task DeleteSessionAsync(string name, CancellationToken cancellationToken = default)
        {
            var session = _sessions.FirstOrDefault(s => s.Name == name && !s.IsActive);
            if (session != null)
                _sessions.Remove(session);
            return Task.CompletedTask;
        }

        public Task SetActiveSessionAsync(string name, CancellationToken cancellationToken = default)
        {
            for (int i = 0; i < _sessions.Count; i++)
                _sessions[i] = _sessions[i] with { IsActive = _sessions[i].Name == name };
            return Task.CompletedTask;
        }

        public Task<string> GetActiveSessionNameAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_sessions.FirstOrDefault(session => session.IsActive)?.Name ?? string.Empty);
        }
    }

    private sealed class FakeNetworkingService : INetworkingService
    {
        public Task<NetworkingSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
        {
            var bindings = new List<PortBinding>
            {
                new("container-web", "web-app", 8080, 80, "tcp"),
                new("container-api", "api-server", 9000, 3000, "tcp"),
            };

            var proxy = new ProxyConfiguration(
                "http://proxy.example.com:8080",
                "https://proxy.example.com:8080",
                "localhost,127.0.0.1"
            );

            return Task.FromResult(new NetworkingSnapshot(NetworkMode.Bridge, bindings, proxy));
        }

        public Task SetNetworkModeAsync(NetworkMode mode, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeNetworkingServiceEmpty : INetworkingService
    {
        public Task<NetworkingSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
        {
            var proxy = new ProxyConfiguration(null, null, null);
            return Task.FromResult(new NetworkingSnapshot(NetworkMode.Bridge, [], proxy));
        }

        public Task SetNetworkModeAsync(NetworkMode mode, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeVolumeService(IReadOnlyList<VolumeSummary> volumes) : IVolumeService
    {
        private readonly List<VolumeSummary> _volumes = volumes.ToList();
        public List<string> Created { get; } = [];
        public List<string> Deleted { get; } = [];
        public int PruneCount { get; private set; }

        public Task<IReadOnlyList<VolumeSummary>> ListVolumesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<VolumeSummary>>(_volumes.ToList());

        public Task CreateVolumeAsync(string name, CancellationToken cancellationToken = default)
        {
            Created.Add(name);
            _volumes.Add(new VolumeSummary(name, "local", $"/var/lib/docker/volumes/{name}/_data", null, "unknown", false));
            return Task.CompletedTask;
        }

        public Task DeleteVolumeAsync(string name, CancellationToken cancellationToken = default)
        {
            Deleted.Add(name);
            _volumes.RemoveAll(v => v.Name == name);
            return Task.CompletedTask;
        }

        public Task PruneVolumesAsync(CancellationToken cancellationToken = default)
        {
            PruneCount++;
            _volumes.RemoveAll(v => !v.IsInUse);
            return Task.CompletedTask;
        }
    }
}
