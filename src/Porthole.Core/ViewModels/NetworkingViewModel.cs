using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Porthole.Core.Models;
using Porthole.Core.Services;

namespace Porthole.Core.ViewModels;

public partial class NetworkingViewModel : ObservableObject
{
    private readonly INetworkingService _networkingService;
    private bool _suppressModeToggle;

    public ObservableCollection<PortBinding> PortBindings { get; } = [];

    [ObservableProperty]
    private NetworkMode activeMode = NetworkMode.Bridge;

    [ObservableProperty]
    private bool isConsommeEnabled;

    [ObservableProperty]
    private string? httpProxy;

    [ObservableProperty]
    private string? httpsProxy;

    [ObservableProperty]
    private string? noProxy;

    [ObservableProperty]
    private string statusMessage = "Load networking snapshot to inspect the current configuration.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotLoading))]
    private bool isLoading;

    public string ActiveModeDescription => ActiveMode == NetworkMode.Consomme
        ? "consomme — Linux traffic relayed through Windows. VPN, proxy, and enterprise policies are inherited from the host."
        : "bridge — standard Linux virtual network bridge. Traffic is isolated from the host networking stack.";

    public bool HasNoPortBindings => PortBindings.Count == 0;
    public bool IsNotLoading => !IsLoading;
    public string PortBindingsEmptyLabel => PortBindings.Count == 0 ? "(no active port bindings)" : string.Empty;
    public string HttpProxyDisplay => string.IsNullOrWhiteSpace(HttpProxy) ? "(none)" : HttpProxy;
    public string HttpsProxyDisplay => string.IsNullOrWhiteSpace(HttpsProxy) ? "(none)" : HttpsProxy;
    public string NoProxyDisplay => string.IsNullOrWhiteSpace(NoProxy) ? "(none)" : NoProxy;

    public NetworkingViewModel(INetworkingService networkingService)
    {
        _networkingService = networkingService;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await RefreshAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        StatusMessage = "Loading networking snapshot...";

        try
        {
            var snapshot = await _networkingService.GetSnapshotAsync(cancellationToken);
            ApplySnapshot(snapshot);
            StatusMessage = $"Snapshot loaded. {PortBindings.Count} active port binding{(PortBindings.Count == 1 ? string.Empty : "s")}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load networking snapshot: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SetNetworkModeAsync(CancellationToken cancellationToken = default)
    {
        NetworkMode target = IsConsommeEnabled ? NetworkMode.Consomme : NetworkMode.Bridge;
        if (target == ActiveMode)
        {
            return;
        }

        IsLoading = true;
        StatusMessage = $"Switching network mode to {target.ToString().ToLowerInvariant()}...";

        try
        {
            await _networkingService.SetNetworkModeAsync(target, cancellationToken);
            ActiveMode = target;
            OnPropertyChanged(nameof(ActiveModeDescription));
            StatusMessage = $"Network mode set to {target.ToString().ToLowerInvariant()}. New containers will use this mode.";
        }
        catch (Exception ex)
        {
            // Revert the toggle on failure
            _suppressModeToggle = true;
            IsConsommeEnabled = ActiveMode == NetworkMode.Consomme;
            _suppressModeToggle = false;
            StatusMessage = $"Failed to set network mode: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnIsConsommeEnabledChanged(bool value)
    {
        if (!_suppressModeToggle)
        {
            _ = SetNetworkModeAsync();
        }
    }

    private void ApplySnapshot(NetworkingSnapshot snapshot)
    {
        ActiveMode = snapshot.ActiveMode;

        _suppressModeToggle = true;
        IsConsommeEnabled = snapshot.ActiveMode == NetworkMode.Consomme;
        _suppressModeToggle = false;

        OnPropertyChanged(nameof(ActiveModeDescription));

        PortBindings.Clear();
        foreach (var binding in snapshot.PortBindings)
        {
            PortBindings.Add(binding);
        }

        OnPropertyChanged(nameof(HasNoPortBindings));
        OnPropertyChanged(nameof(PortBindingsEmptyLabel));

        HttpProxy = string.IsNullOrWhiteSpace(snapshot.HostProxy.HttpProxy) ? null : snapshot.HostProxy.HttpProxy;
        HttpsProxy = string.IsNullOrWhiteSpace(snapshot.HostProxy.HttpsProxy) ? null : snapshot.HostProxy.HttpsProxy;
        NoProxy = string.IsNullOrWhiteSpace(snapshot.HostProxy.NoProxy) ? null : snapshot.HostProxy.NoProxy;
        OnPropertyChanged(nameof(HttpProxyDisplay));
        OnPropertyChanged(nameof(HttpsProxyDisplay));
        OnPropertyChanged(nameof(NoProxyDisplay));
    }
}
