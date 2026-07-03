using System.Collections.ObjectModel;
using System.Security.Principal;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Porthole.Core.Models;
using Porthole.Core.Services;

namespace Porthole.Core.ViewModels;

public partial class ContainersViewModel : ObservableObject
{
    private readonly IContainerCatalogService _containerCatalogService;
    private string? _lastSelectedContainerId;
    private readonly bool _isElevated;

    public ObservableCollection<ContainerSummary> Containers { get; } = [];

    [ObservableProperty]
    private ContainerSummary? selectedContainer;

    [ObservableProperty]
    private string actionStatus = "Load active and stopped containers from the tray host.";

    [ObservableProperty]
    private string podsStatus = "Pod tree is loading from kubectl.";

    [ObservableProperty]
    private string podTreeText = "(loading pods...)";

    public ContainersViewModel(IContainerCatalogService containerCatalogService)
    {
        _containerCatalogService = containerCatalogService;
        _isElevated = DetectIsElevated();
    }

    public bool IsElevated => _isElevated;

    public bool IsElevationMismatchLikely => !_isElevated;

    public string ElevationWarningText => IsElevationMismatchLikely
        ? "Containers running under an administrative context may not be visible here."
        : "Run terminal commands with the same elevation as this app for consistent container visibility.";

    public string ContainerCountLabel => Containers.Count == 1 ? "1 container" : $"{Containers.Count} containers";

    public string RunningCountLabel
    {
        get
        {
            int running = Containers.Count(container => container.IsRunning);
            int stopped = Math.Max(0, Containers.Count - running);
            return $"{running} running / {stopped} stopped";
        }
    }

    public string SelectedContainerTitle => SelectedContainer?.DisplayName ?? "No container selected";

    public string SelectedContainerSubtitle => SelectedContainer is null
        ? "Select a container to inspect its state and image reference."
        : SelectedContainer.MetadataLine;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await RefreshAsync(cancellationToken);
    }

    public Task SubscribeAsync(
        Func<IReadOnlyList<ContainerSummary>, IReadOnlyList<PodSummary>, Task> onUpdate,
        CancellationToken cancellationToken = default)
    {
        return _containerCatalogService.SubscribeAsync(onUpdate, cancellationToken);
    }

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        _lastSelectedContainerId = SelectedContainer?.Id;
        IReadOnlyList<ContainerSummary> containers = await _containerCatalogService.ListContainersAsync(cancellationToken);
        IReadOnlyList<PodSummary> pods = await _containerCatalogService.ListPodsAsync(cancellationToken);

        ApplyCatalogUpdate(containers, pods);
    }

    public void ApplyCatalogUpdate(IReadOnlyList<ContainerSummary> containers, IReadOnlyList<PodSummary> pods)
    {
        bool containersChanged = !containers.SequenceEqual(Containers);
        if (containersChanged)
        {
            _lastSelectedContainerId ??= SelectedContainer?.Id;

            Containers.Clear();
            foreach (ContainerSummary container in containers)
            {
                Containers.Add(container);
            }

            SelectedContainer = Containers.FirstOrDefault(container => container.Id == _lastSelectedContainerId)
                ?? Containers.FirstOrDefault();

            OnPropertyChanged(nameof(ContainerCountLabel));
            OnPropertyChanged(nameof(RunningCountLabel));
            OnPropertyChanged(nameof(SelectedContainerTitle));
            OnPropertyChanged(nameof(SelectedContainerSubtitle));
            ActionStatus = Containers.Count == 0
                ? "No containers found. Start one from Run Wizard or wslc to see it here."
                : $"Loaded {Containers.Count} container records.";
        }

        string nextPodTree = BuildPodTree(pods);
        if (!string.Equals(PodTreeText, nextPodTree, StringComparison.Ordinal))
        {
            PodTreeText = nextPodTree;
        }

        string nextPodsStatus = pods.Count == 0
            ? "No pods found (kubectl unavailable or no cluster pods are running)."
            : $"Loaded {pods.Count} pods.";

        if (!string.Equals(PodsStatus, nextPodsStatus, StringComparison.Ordinal))
        {
            PodsStatus = nextPodsStatus;
        }
    }

    [RelayCommand]
    private async Task StartContainerAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedContainer is null)
        {
            ActionStatus = "Select a container before starting it.";
            return;
        }

        if (SelectedContainer.IsRunning)
        {
            ActionStatus = $"{SelectedContainer.DisplayName} is already running.";
            return;
        }

        await _containerCatalogService.StartContainerAsync(SelectedContainer, cancellationToken);
        ActionStatus = $"Started {SelectedContainer.DisplayName}.";
        await RefreshAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task StopContainerAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedContainer is null)
        {
            ActionStatus = "Select a container before stopping it.";
            return;
        }

        if (!SelectedContainer.IsRunning)
        {
            ActionStatus = $"{SelectedContainer.DisplayName} is not running.";
            return;
        }

        await _containerCatalogService.StopContainerAsync(SelectedContainer, cancellationToken);
        ActionStatus = $"Stopped {SelectedContainer.DisplayName}.";
        await RefreshAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task RemoveContainerAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedContainer is null)
        {
            ActionStatus = "Select a container before removing it.";
            return;
        }

        string displayName = SelectedContainer.DisplayName;
        await _containerCatalogService.RemoveContainerAsync(SelectedContainer, cancellationToken);
        ActionStatus = $"Removed {displayName}.";
        await RefreshAsync(cancellationToken);
    }

    partial void OnSelectedContainerChanged(ContainerSummary? value)
    {
        OnPropertyChanged(nameof(SelectedContainerTitle));
        OnPropertyChanged(nameof(SelectedContainerSubtitle));
    }

    private static string BuildPodTree(IReadOnlyList<PodSummary> pods)
    {
        if (pods.Count == 0)
        {
            return "k8s\n  (no pods)";
        }

        var lines = new List<string> { "k8s" };
        var namespaces = pods
            .GroupBy(pod => string.IsNullOrWhiteSpace(pod.Namespace) ? "default" : pod.Namespace)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (int namespaceIndex = 0; namespaceIndex < namespaces.Count; namespaceIndex++)
        {
            var group = namespaces[namespaceIndex];
            bool isLastNamespace = namespaceIndex == namespaces.Count - 1;
            string namespaceBranch = isLastNamespace ? "└─ " : "├─ ";
            string podIndent = isLastNamespace ? "   " : "│  ";

            lines.Add($"{namespaceBranch}{group.Key}");

            PodSummary[] namespacePods = group
                .OrderBy(pod => pod.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            for (int podIndex = 0; podIndex < namespacePods.Length; podIndex++)
            {
                PodSummary pod = namespacePods[podIndex];
                bool isLastPod = podIndex == namespacePods.Length - 1;
                string podBranch = isLastPod ? "└─ " : "├─ ";
                lines.Add($"{podIndent}{podBranch}{pod.Name} [{pod.Phase}]");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static bool DetectIsElevated()
    {
        try
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}