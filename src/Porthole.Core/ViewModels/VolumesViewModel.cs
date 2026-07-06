using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Porthole.Core.Models;
using Porthole.Core.Services;

namespace Porthole.Core.ViewModels;

public partial class VolumesViewModel : ObservableObject
{
    private readonly IVolumeService _volumeService;

    public ObservableCollection<VolumeSummary> Volumes { get; } = [];

    [ObservableProperty]
    private string statusMessage = "Load volumes to see named volumes and bind-mounts for the active session.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotLoading))]
    private bool isLoading;

    [ObservableProperty]
    private string newVolumeName = string.Empty;

    [ObservableProperty]
    private VolumeSummary? selectedVolume;

    public bool IsNotLoading => !IsLoading;

    public string VolumeCountLabel => IsLoading
        ? "Loading..."
        : Volumes.Count == 1 ? "1 volume" : $"{Volumes.Count} volumes";

    public VolumesViewModel(IVolumeService volumeService)
    {
        _volumeService = volumeService;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await RefreshAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        StatusMessage = "Loading volumes...";

        try
        {
            var volumes = await _volumeService.ListVolumesAsync(cancellationToken);
            Volumes.Clear();
            foreach (var v in volumes)
            {
                Volumes.Add(v);
            }

            OnPropertyChanged(nameof(VolumeCountLabel));
            int unusedCount = volumes.Count(v => !v.IsInUse);
            StatusMessage = $"{volumes.Count} volume{(volumes.Count == 1 ? string.Empty : "s")} found."
                + (unusedCount > 0 ? $" {unusedCount} unused." : string.Empty);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load volumes: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(VolumeCountLabel));
        }
    }

    [RelayCommand]
    private async Task CreateVolumeAsync(CancellationToken cancellationToken = default)
    {
        string name = NewVolumeName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            StatusMessage = "Please enter a volume name.";
            return;
        }

        IsLoading = true;
        StatusMessage = $"Creating volume '{name}'...";

        try
        {
            await _volumeService.CreateVolumeAsync(name, cancellationToken);
            NewVolumeName = string.Empty;
            StatusMessage = $"Volume '{name}' created.";
            await RefreshAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to create volume: {ex.Message}";
            IsLoading = false;
            OnPropertyChanged(nameof(VolumeCountLabel));
        }
    }

    [RelayCommand]
    private async Task DeleteVolumeAsync(VolumeSummary volume, CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        StatusMessage = $"Deleting volume '{volume.Name}'...";

        try
        {
            await _volumeService.DeleteVolumeAsync(volume.Name, cancellationToken);
            StatusMessage = $"Volume '{volume.Name}' deleted.";
            if (SelectedVolume == volume)
            {
                SelectedVolume = null;
            }

            await RefreshAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to delete volume: {ex.Message}";
            IsLoading = false;
            OnPropertyChanged(nameof(VolumeCountLabel));
        }
    }

    [RelayCommand]
    private async Task PruneVolumesAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        StatusMessage = "Pruning unused volumes...";

        try
        {
            await _volumeService.PruneVolumesAsync(cancellationToken);
            StatusMessage = "Unused volumes pruned.";
            await RefreshAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to prune volumes: {ex.Message}";
            IsLoading = false;
            OnPropertyChanged(nameof(VolumeCountLabel));
        }
    }
}
