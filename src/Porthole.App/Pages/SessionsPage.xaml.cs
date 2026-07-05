using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Porthole.Core.Models;
using Porthole.Core.ViewModels;

namespace Porthole_App.Pages;

public sealed partial class SessionsPage : Page
{
    private bool _initialized;

    public SessionViewModel ViewModel { get; }

    public SessionsPage()
    {
        ViewModel = (SessionViewModel)App.Services.GetService(typeof(SessionViewModel))!;
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

    private async void SwitchButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SessionSummary session })
        {
            await ViewModel.SwitchSessionCommand.ExecuteAsync(session);
        }
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SessionSummary session })
        {
            var dialog = new ContentDialog
            {
                Title = "Delete Session",
                Content = $"Delete session '{session.Name}'? This cannot be undone.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await ViewModel.DeleteSessionCommand.ExecuteAsync(session);
            }
        }
    }
}
