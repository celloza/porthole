using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Porthole.Core.Models;
using Porthole.Core.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace Porthole_App.Pages;

public sealed partial class VolumesPage : Page
{
    private bool _initialized;

    public VolumesViewModel ViewModel { get; }

    public VolumesPage()
    {
        ViewModel = (VolumesViewModel)App.Services.GetService(typeof(VolumesViewModel))!;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_initialized)
        {
            _initialized = true;
            await ViewModel.InitializeAsync();
        }
    }

    private async void DeleteVolumeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: VolumeSummary volume })
        {
            var dialog = new ContentDialog
            {
                Title = "Delete Volume",
                Content = $"Delete volume '{volume.Name}'? This cannot be undone.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await ViewModel.DeleteVolumeCommand.ExecuteAsync(volume);
            }
        }
    }

    private void CopyMountStringButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: VolumeSummary volume })
        {
            if (!volume.CanCopyMountString)
            {
                ViewModel.StatusMessage = "No mount string is available for this volume yet.";
                return;
            }

            var dataPackage = new DataPackage();
            dataPackage.SetText(volume.MountString);
            Clipboard.SetContent(dataPackage);
            ViewModel.StatusMessage = $"Copied mount string '{volume.MountString}'.";
        }
    }
}
