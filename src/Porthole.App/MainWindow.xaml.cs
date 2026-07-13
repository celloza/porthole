using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Graphics;
using Windows.UI;
using WinRT.Interop;
using Porthole.Core.ViewModels;
using Porthole_App.Pages;

namespace Porthole_App;

public sealed partial class MainWindow : Window
{
    private bool _shellInitialized;

    public ShellViewModel ViewModel { get; }

    public void ApplyTheme(ElementTheme theme)
    {
        RootGrid.RequestedTheme = theme;
        ContentHostGrid.RequestedTheme = theme;
        NavView.RequestedTheme = theme;
        AppTitleBar.RequestedTheme = theme;
        ApplyCaptionButtonForeground(GetEffectiveTheme());
    }

    public ElementTheme GetEffectiveTheme()
    {
        ElementTheme requestedTheme = RootGrid.RequestedTheme;
        return requestedTheme switch
        {
            ElementTheme.Dark => ElementTheme.Dark,
            ElementTheme.Light => ElementTheme.Light,
            _ => RootGrid.ActualTheme == ElementTheme.Dark ? ElementTheme.Dark : ElementTheme.Light,
        };
    }

    public MainWindow()
    {
        App.TraceStartup("MainWindow ctor start");
        ViewModel = (ShellViewModel)App.Services.GetService(typeof(ShellViewModel))!;
        InitializeComponent();
        App.TraceStartup("MainWindow InitializeComponent complete");

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        ApplyCaptionButtonForeground(GetEffectiveTheme());
        AppWindow.Resize(new SizeInt32(1360, 900));
        CenterWindow();
        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(iconPath))
        {
            AppWindow.SetIcon(iconPath);
        }
        AppWindow.Title = ViewModel.WindowTitle;
        NavFrame.Navigate(typeof(HomePage));
        _ = InitializeShellAsync();
        Closed += (_, _) => ViewModel.Cleanup();
        App.TraceStartup($"MainWindow HWND {WindowNative.GetWindowHandle(this)}");
        App.TraceStartup("MainWindow ctor complete");
    }

    private async Task InitializeShellAsync()
    {
        if (_shellInitialized)
        {
            return;
        }

        _shellInitialized = true;
        try
        {
            await ViewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            _shellInitialized = false;
            App.TraceStartup($"InitializeShellAsync failed: {ex}");
        }
    }

    private void ApplyCaptionButtonForeground(ElementTheme effectiveTheme)
    {
        AppWindowTitleBar titleBar = AppWindow.TitleBar;
        if (effectiveTheme == ElementTheme.Light)
        {
            titleBar.ButtonForegroundColor = Color.FromArgb(0xFF, 0x00, 0x00, 0x00);
            titleBar.ButtonHoverForegroundColor = Color.FromArgb(0xFF, 0x00, 0x00, 0x00);
            titleBar.ButtonPressedForegroundColor = Color.FromArgb(0xFF, 0x00, 0x00, 0x00);
            titleBar.ButtonInactiveForegroundColor = Color.FromArgb(0xFF, 0x69, 0x69, 0x69);
        }
        else
        {
            titleBar.ButtonForegroundColor = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
            titleBar.ButtonHoverForegroundColor = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
            titleBar.ButtonPressedForegroundColor = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
            titleBar.ButtonInactiveForegroundColor = Color.FromArgb(0xFF, 0xD3, 0xD3, 0xD3);
        }
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

    private void NavFrame_Navigated(object sender, NavigationEventArgs args)
    {
        SyncNavViewToCurrentPage();
    }

    private void SyncNavViewToCurrentPage()
    {
        Type? currentPage = NavFrame.CurrentSourcePageType;

        if (currentPage == typeof(SettingsPage))
        {
            NavView.SelectedItem = NavView.SettingsItem;
            ViewModel.SetSection("Settings");
            SessionToolbarBorder.Visibility = Visibility.Collapsed;
            return;
        }

        (string tag, string section) = currentPage?.Name switch
        {
            nameof(HomePage) => ("home", "System Dashboard"),
            nameof(ImagesPage) => ("images", "Images"),
            nameof(ContainersPage) => ("containers", "Containers"),
            nameof(SessionsPage) => ("sessions", "Sessions"),
            nameof(NetworkingPage) => ("networking", "Networking"),
            nameof(VolumesPage) => ("volumes", "Volumes"),
            nameof(RunWizardPage) => ("run-wizard", "Run Wizard"),
            _ => (string.Empty, string.Empty),
        };

        if (string.IsNullOrEmpty(tag))
            return;

        ViewModel.SetSection(section);
        SessionToolbarBorder.Visibility = currentPage == typeof(SessionsPage)
            ? Visibility.Collapsed
            : Visibility.Visible;

        NavigationViewItem? match = NavView.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(item => item.Tag as string == tag);

        if (match is not null)
            NavView.SelectedItem = match;
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            if (NavFrame.CurrentSourcePageType != typeof(SettingsPage))
            {
                ViewModel.SetSection("Settings");
                NavFrame.Navigate(typeof(SettingsPage));
            }
        }
        else if (args.SelectedItem is NavigationViewItem item)
        {
            switch (item.Tag)
            {
                case "home":
                    if (NavFrame.CurrentSourcePageType != typeof(HomePage))
                    {
                        ViewModel.SetSection("System Dashboard");
                        NavFrame.Navigate(typeof(HomePage));
                    }
                    break;
                case "images":
                    if (NavFrame.CurrentSourcePageType != typeof(ImagesPage))
                    {
                        ViewModel.SetSection("Images");
                        NavFrame.Navigate(typeof(ImagesPage));
                    }
                    break;
                case "containers":
                    if (NavFrame.CurrentSourcePageType != typeof(ContainersPage))
                    {
                        ViewModel.SetSection("Containers");
                        NavFrame.Navigate(typeof(ContainersPage));
                    }
                    break;
                case "sessions":
                    if (NavFrame.CurrentSourcePageType != typeof(SessionsPage))
                    {
                        ViewModel.SetSection("Sessions");
                        NavFrame.Navigate(typeof(SessionsPage));
                    }
                    break;
                case "networking":
                    if (NavFrame.CurrentSourcePageType != typeof(NetworkingPage))
                    {
                        ViewModel.SetSection("Networking");
                        NavFrame.Navigate(typeof(NetworkingPage));
                    }
                    break;
                case "volumes":
                    if (NavFrame.CurrentSourcePageType != typeof(VolumesPage))
                    {
                        ViewModel.SetSection("Volumes");
                        NavFrame.Navigate(typeof(VolumesPage));
                    }
                    break;
                case "run-wizard":
                    if (NavFrame.CurrentSourcePageType != typeof(RunWizardPage))
                    {
                        ViewModel.SetSection("Run Wizard");
                        NavFrame.Navigate(typeof(RunWizardPage));
                    }
                    break;
                default:
                    throw new InvalidOperationException($"Unknown navigation item tag: {item.Tag}");
            }
        }
    }

    private void OpenSessionsToolbarButton_Click(object sender, RoutedEventArgs e)
    {
        if (NavFrame.CurrentSourcePageType != typeof(SessionsPage))
        {
            ViewModel.SetSection("Sessions");
            NavFrame.Navigate(typeof(SessionsPage));
        }
    }
}
