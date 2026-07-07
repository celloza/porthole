using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel;
using Windows.Storage;
using Windows.System;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Porthole_App.Pages;

public sealed partial class SettingsPage : Page
{
    private const string ThemeSettingKey = "ThemePreference";
    private bool _isInitializingThemeSelection = true;

    public string AppVersionText { get; }

    public string FooterText { get; }

    public SettingsPage()
    {
        AppVersionText = $"{GetVersionText()} - {DateTime.Now:yyyy}";
        FooterText = $"Copyright © Porthole contributors. Licensed under GNU GPL v3.0. Process architecture: {RuntimeInformation.ProcessArchitecture}.";

        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        App.TraceStartup("SettingsPage loaded");
        _isInitializingThemeSelection = true;
        InitializeThemePreference();
        ActualThemeChanged += SettingsPage_ActualThemeChanged;
        if (ThemeModeComboBox.SelectedIndex < 0)
        {
            ThemeModeComboBox.SelectedIndex = 0;
        }

        UpdateAboutLogo();
        DispatcherQueue.TryEnqueue(UpdateAboutLogo);
        _isInitializingThemeSelection = false;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ActualThemeChanged -= SettingsPage_ActualThemeChanged;
    }

    private void SettingsPage_ActualThemeChanged(FrameworkElement sender, object args)
    {
        if (GetSavedThemePreference() == ElementTheme.Default)
        {
            UpdateAboutLogo();
        }
    }

    private void ThemeModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializingThemeSelection)
        {
            return;
        }

        ElementTheme selectedTheme = ThemeModeComboBox.SelectedIndex switch
        {
            1 => ElementTheme.Light,
            2 => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };

        App.TraceStartup($"Settings theme changed: index={ThemeModeComboBox.SelectedIndex}, theme={selectedTheme}");

        ApplyThemePreference(selectedTheme, persist: true);
    }

    private void InitializeThemePreference()
    {
        ElementTheme savedTheme = GetSavedThemePreference();

        ThemeModeComboBox.SelectionChanged -= ThemeModeComboBox_SelectionChanged;
        ThemeModeComboBox.SelectedIndex = savedTheme switch
        {
            ElementTheme.Light => 1,
            ElementTheme.Dark => 2,
            _ => 0,
        };
        ThemeModeComboBox.SelectionChanged += ThemeModeComboBox_SelectionChanged;

        ApplyThemePreference(savedTheme, persist: false);
    }

    private void ApplyThemePreference(ElementTheme theme, bool persist)
    {
        if (App.AppWindow is MainWindow mainWindow)
        {
            mainWindow.ApplyTheme(theme);
        }
        else if (XamlRoot?.Content is FrameworkElement xamlRootElement)
        {
            xamlRootElement.RequestedTheme = theme;
        }

        if (App.AppWindow?.Content is FrameworkElement rootElement)
        {
            rootElement.RequestedTheme = theme;
        }

        RequestedTheme = theme;
        UpdateAboutLogo();

        if (persist)
        {
            TrySaveThemePreference(theme);
        }
    }

    private ElementTheme GetSavedThemePreference()
    {
        try
        {
            object? storedValue = ApplicationData.Current.LocalSettings.Values[ThemeSettingKey];
            if (storedValue is string themeName && Enum.TryParse(themeName, out ElementTheme parsedTheme))
            {
                return parsedTheme;
            }
        }
        catch
        {
            // ApplicationData.Current can be unavailable in some unpackaged debug contexts.
        }

        return ElementTheme.Default;
    }

    private static void TrySaveThemePreference(ElementTheme theme)
    {
        try
        {
            ApplicationData.Current.LocalSettings.Values[ThemeSettingKey] = theme.ToString();
        }
        catch
        {
            // Persisting theme is best-effort for unpackaged runs.
        }
    }

    private ElementTheme GetEffectiveTheme()
    {
        if (App.AppWindow is MainWindow mainWindow)
        {
            return mainWindow.GetEffectiveTheme();
        }

        ElementTheme requestedTheme = (App.AppWindow?.Content as FrameworkElement)?.RequestedTheme ?? RequestedTheme;

        return requestedTheme switch
        {
            ElementTheme.Light => ElementTheme.Light,
            ElementTheme.Dark => ElementTheme.Dark,
            _ => ActualTheme == ElementTheme.Dark ? ElementTheme.Dark : ElementTheme.Light,
        };
    }

    private void UpdateAboutLogo()
    {
        if (AboutLogoImage is null)
        {
            return;
        }

        string appxPath = GetEffectiveTheme() == ElementTheme.Dark
            ? "ms-appx:///Assets/portholelogowithname-dark.svg"
            : "ms-appx:///Assets/portholelogowithname.svg";

        try
        {
            AboutLogoImage.Source = new SvgImageSource(new Uri(appxPath, UriKind.Absolute));
        }
        catch
        {
            AboutLogoImage.Source = new SvgImageSource(new Uri("ms-appx:///Assets/portholelogowithname.svg", UriKind.Absolute));
        }
    }

    private static string GetVersionText()
    {
        try
        {
            PackageVersion packageVersion = Package.Current.Id.Version;
            return $"v{packageVersion.Major}.{packageVersion.Minor}.{packageVersion.Build}";
        }
        catch
        {
            // Unpackaged local debug runs don't have package identity.
        }

        Assembly assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();

        string? informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            string normalized = FormatVersionLabel(informationalVersion);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return normalized;
            }
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(assembly.Location))
            {
                string? productVersion = FileVersionInfo.GetVersionInfo(assembly.Location).ProductVersion;
                if (!string.IsNullOrWhiteSpace(productVersion))
                {
                    string normalized = FormatVersionLabel(productVersion);
                    if (!string.IsNullOrWhiteSpace(normalized))
                    {
                        return normalized;
                    }
                }
            }
        }
        catch
        {
            // Ignore file-version lookup failures and fall back.
        }

        Version? assemblyVersion = assembly.GetName().Version;
        return assemblyVersion is null
            ? "v0.0.0"
            : $"v{assemblyVersion.Major}.{Math.Max(0, assemblyVersion.Minor)}.{Math.Max(0, assemblyVersion.Build)}";
    }

    private static string FormatVersionLabel(string rawVersion)
    {
        string trimmed = rawVersion.Trim().TrimStart('v', 'V');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        string[] parts = trimmed.Split('+', 2, StringSplitOptions.TrimEntries);
        string baseVersion = parts[0];
        if (string.IsNullOrWhiteSpace(baseVersion))
        {
            return string.Empty;
        }

        if (parts.Length == 1 || string.IsNullOrWhiteSpace(parts[1]))
        {
            return $"v{baseVersion}";
        }

        string metadata = parts[1];
        string compactMetadata = metadata.Length > 12 ? metadata[..12] : metadata;
        return $"v{baseVersion} ({compactMetadata})";
    }

    private async void GitHubRepositoryButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await LaunchUrlAsync("https://github.com/celloza/porthole");
    }

    private async void LicenseButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await LaunchUrlAsync("https://github.com/celloza/porthole/blob/main/LICENSE");
    }

    private async void IssuesButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await LaunchUrlAsync("https://github.com/celloza/porthole/issues");
    }

    private async void ReleasesButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await LaunchUrlAsync("https://github.com/celloza/porthole/releases");
    }

    private static async Task LaunchUrlAsync(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            await Launcher.LaunchUriAsync(uri);
        }
    }
}
