using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Porthole.Core.Models;
using Porthole.Core.Services;
using Windows.ApplicationModel.DataTransfer;

namespace Porthole.Core.ViewModels;

public partial class ImagesViewModel : ObservableObject
{
    private readonly IImageCatalogService _imageCatalogService;
    private string? _lastLoadedImageReference;

    public ObservableCollection<ImageSummary> Images { get; } = [];

    [ObservableProperty]
    private string imageReference = "mcr.microsoft.com/oss/nginx/nginx:latest";

    [ObservableProperty]
    private string pullStatus = "Ready to pull an OCI image into the local cache.";

    [ObservableProperty]
    private string actionStatus = "Refresh, prune, tag, and delete actions are bound to the same service contract the backend host will implement.";

    [ObservableProperty]
    private int pullProgressPercent;

    [ObservableProperty]
    private ImageSummary? selectedImage;

    [ObservableProperty]
    private string newTag = "local/dev:latest";

    [ObservableProperty]
    private bool isInitialLoadInProgress;

    public ImagesViewModel(IImageCatalogService imageCatalogService)
    {
        _imageCatalogService = imageCatalogService;
    }

    public double PullProgressOpacity => PullProgressPercent > 0 && PullProgressPercent < 100 ? 1 : 0;

    public string ImageCountLabel => IsInitialLoadInProgress
        ? "Loading..."
        : Images.Count == 1 ? "1 image" : $"{Images.Count} images";

    public double InitialLoadIndicatorOpacity => IsInitialLoadInProgress ? 1 : 0;

    public string SelectedImageTitle => SelectedImage?.DisplayName ?? "No image selected";

    public string SelectedImageSubtitle => SelectedImage is null
        ? "Select an image to tag or delete it."
        : $"{SelectedImage.MetadataLine} · {SelectedImage.SizeLabel}";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        IsInitialLoadInProgress = true;
        OnPropertyChanged(nameof(ImageCountLabel));
        OnPropertyChanged(nameof(InitialLoadIndicatorOpacity));

        try
        {
            await RefreshAsync(cancellationToken);
        }
        finally
        {
            IsInitialLoadInProgress = false;
            OnPropertyChanged(nameof(ImageCountLabel));
            OnPropertyChanged(nameof(InitialLoadIndicatorOpacity));
        }
    }

    public Task SubscribeAsync(Func<IReadOnlyList<ImageSummary>, Task> onUpdate, CancellationToken cancellationToken = default)
    {
        return _imageCatalogService.SubscribeAsync(onUpdate, cancellationToken);
    }

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        _lastLoadedImageReference = SelectedImage?.Reference;
        var images = await _imageCatalogService.ListImagesAsync(cancellationToken);

        ApplyInventoryUpdate(images);
    }

    public void ApplyInventoryUpdate(IReadOnlyList<ImageSummary> images)
    {
        if (ActionStatus.StartsWith("Image subscription disconnected", StringComparison.OrdinalIgnoreCase))
        {
            ActionStatus = "Live image updates connected.";
        }

        if (images.SequenceEqual(Images))
        {
            return;
        }

        Images.Clear();
        foreach (var image in images)
        {
            Images.Add(image);
        }

        SelectedImage = Images.FirstOrDefault(image => image.Reference == _lastLoadedImageReference)
            ?? Images.FirstOrDefault();
        OnPropertyChanged(nameof(ImageCountLabel));
        OnPropertyChanged(nameof(SelectedImageTitle));
        OnPropertyChanged(nameof(SelectedImageSubtitle));
        
        ActionStatus = $"Loaded {images.Count} cached image records.";
    }

    [RelayCommand]
    private async Task PullImageAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ImageReference))
        {
            PullStatus = "Enter an image reference before pulling.";
            return;
        }

        string normalizedReference = ImageReference.Trim();
        PullProgressPercent = 1;
        PullStatus = $"Starting pull for {normalizedReference}...";
        OnPropertyChanged(nameof(PullProgressOpacity));

        var progress = new Progress<ImagePullProgress>(update =>
        {
            PullProgressPercent = update.Percent;
            PullStatus = update.Status;
            OnPropertyChanged(nameof(PullProgressOpacity));
        });

        try
        {
            await _imageCatalogService.PullImageAsync(normalizedReference, progress, cancellationToken);

            PullProgressPercent = 100;
            PullStatus = $"Pulled {normalizedReference}.";
            OnPropertyChanged(nameof(PullProgressOpacity));
            await RefreshAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            PullStatus = "Image pull was canceled.";
        }
        catch (TimeoutException)
        {
            PullStatus = "Image pull timed out before completion. Check tray connectivity and try again.";
        }
        catch (Exception ex)
        {
            PullStatus = FormatPullErrorMessage(ex.Message, normalizedReference);
        }
        finally
        {
            PullProgressPercent = 0;
            OnPropertyChanged(nameof(PullProgressOpacity));
        }
    }

    [RelayCommand]
    private async Task PruneImagesAsync(CancellationToken cancellationToken = default)
    {
        await _imageCatalogService.PruneImagesAsync(cancellationToken);
        await RefreshAsync(cancellationToken);
        ActionStatus = "Image prune request completed.";
    }

    [RelayCommand]
    private async Task TagImageAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedImage is null)
        {
            ActionStatus = "Select an image before tagging it.";
            return;
        }

        if (string.IsNullOrWhiteSpace(NewTag))
        {
            ActionStatus = "Enter a tag in repository:tag format.";
            return;
        }

        string sourceName = SelectedImage.DisplayName;
        await _imageCatalogService.TagImageAsync(SelectedImage, NewTag, cancellationToken);
        await RefreshAsync(cancellationToken);
        ActionStatus = $"Tagged {sourceName} as {NewTag.Trim()}.";
    }

    [RelayCommand]
    private async Task DeleteImageAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedImage is null)
        {
            ActionStatus = "Select an image before deleting it.";
            return;
        }

        string imageName = SelectedImage.DisplayName;
        await _imageCatalogService.DeleteImageAsync(SelectedImage, cancellationToken);
        SelectedImage = null;
        await RefreshAsync(cancellationToken);
        ActionStatus = $"Deleted {imageName}.";
    }

    [RelayCommand]
    private void CopySha()
    {
        if (SelectedImage is null) return;
        var dataPackage = new DataPackage();
        dataPackage.SetText(SelectedImage.Id);
        Clipboard.SetContent(dataPackage);
        ActionStatus = "SHA copied to clipboard.";
    }

    partial void OnSelectedImageChanged(ImageSummary? value)
    {
        OnPropertyChanged(nameof(SelectedImageTitle));
        OnPropertyChanged(nameof(SelectedImageSubtitle));
    }

    partial void OnIsInitialLoadInProgressChanged(bool value)
    {
        OnPropertyChanged(nameof(ImageCountLabel));
        OnPropertyChanged(nameof(InitialLoadIndicatorOpacity));
    }

    private static string FormatPullErrorMessage(string? rawMessage, string imageReference)
    {
        string message = (rawMessage ?? string.Empty).Trim();
        string lowered = message.ToLowerInvariant();

        if (lowered.Contains("manifest unknown") || lowered.Contains("not found"))
        {
            return $"Image pull failed: '{imageReference}' was not found. Check the repository and tag (for example: alpine:latest).";
        }

        if (lowered.Contains("unauthorized") || lowered.Contains("authentication required") || lowered.Contains("denied"))
        {
            return "Image pull failed: access denied by the registry. Sign in or use an image you have permission to pull.";
        }

        if (lowered.Contains("name unknown") || lowered.Contains("invalid reference format"))
        {
            return "Image pull failed: invalid image reference format. Use repository:tag (for example: redis:7.4).";
        }

        if (string.IsNullOrWhiteSpace(message) || lowered.Contains("the text associated with this error code couldn't be found"))
        {
            return "Image pull failed: the registry returned an unknown error. Verify the image reference and try again.";
        }

        return $"Image pull failed: {message}";
    }
}