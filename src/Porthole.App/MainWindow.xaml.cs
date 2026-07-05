using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using WinRT.Interop;
using Porthole.Core.ViewModels;
using Porthole_App.Pages;

namespace Porthole_App;

public sealed partial class MainWindow : Window
{
    public ShellViewModel ViewModel { get; }

    public MainWindow()
    {
        App.TraceStartup("MainWindow ctor start");
        ViewModel = (ShellViewModel)App.Services.GetService(typeof(ShellViewModel))!;
        InitializeComponent();
        App.TraceStartup("MainWindow InitializeComponent complete");

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.Resize(new SizeInt32(1360, 900));
        CenterWindow();
        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(iconPath))
        {
            AppWindow.SetIcon(iconPath);
        }
        AppWindow.Title = ViewModel.WindowTitle;
        NavFrame.Navigate(typeof(HomePage));
        App.TraceStartup($"MainWindow HWND {WindowNative.GetWindowHandle(this)}");
        App.TraceStartup("MainWindow ctor complete");
    }

    private void CenterWindow()
    {
        DisplayArea displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        RectInt32 workArea = displayArea.WorkArea;

        int width = AppWindow.Size.Width;
        int height = AppWindow.Size.Height;
        int x = workArea.X + Math.Max(0, (workArea.Width - width) / 2);
        int y = workArea.Y + Math.Max(0, (workArea.Height - height) / 2);

        AppWindow.Move(new PointInt32(x, y));
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
    }

    private void TitleBar_BackRequested(TitleBar sender, object args)
    {
        NavFrame.GoBack();
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            ViewModel.SetSection("Settings");
            NavFrame.Navigate(typeof(SettingsPage));
        }
        else if (args.SelectedItem is NavigationViewItem item)
        {
            switch (item.Tag)
            {
                case "home":
                    ViewModel.SetSection("System Dashboard");
                    NavFrame.Navigate(typeof(HomePage));
                    break;
                case "images":
                    ViewModel.SetSection("Images");
                    NavFrame.Navigate(typeof(ImagesPage));
                    break;
                case "containers":
                    ViewModel.SetSection("Containers");
                    NavFrame.Navigate(typeof(ContainersPage));
                    break;
                case "sessions":
                    ViewModel.SetSection("Sessions");
                    NavFrame.Navigate(typeof(SessionsPage));
                    break;
                case "networking":
                    ViewModel.SetSection("Networking");
                    NavFrame.Navigate(typeof(NetworkingPage));
                    break;
                case "run-wizard":
                    ViewModel.SetSection("Run Wizard");
                    NavFrame.Navigate(typeof(RunWizardPage));
                    break;
                default:
                    throw new InvalidOperationException($"Unknown navigation item tag: {item.Tag}");
            }
        }
    }
}
