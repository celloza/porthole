using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Porthole.Core.ViewModels;

namespace Porthole_App.Pages;

public sealed partial class NetworkingPage : Page
{
    private bool _initialized;

    public NetworkingViewModel ViewModel { get; }

    public NetworkingPage()
    {
        ViewModel = (NetworkingViewModel)App.Services.GetService(typeof(NetworkingViewModel))!;
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
}
