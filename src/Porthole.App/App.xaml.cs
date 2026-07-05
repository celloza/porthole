using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Porthole.Core.Services;
using Porthole.Core.Services.NamedPipe;
using Porthole.Core.ViewModels;

namespace Porthole_App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    public static string DiagnosticsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Porthole");

    private Window? _window;

    public App()
    {
        TraceStartup("App ctor start");
        InitializeComponent();
        Services = ConfigureServices();
        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
        TraceStartup("App ctor complete");
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        TraceStartup("OnLaunched start");
        _window = new MainWindow();
        TraceStartup("MainWindow created");
        _window.Activate();
        TraceStartup("MainWindow activated");
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IWslcService, WslcService>();
        services.AddSingleton<IImageCatalogService, NamedPipeImageCatalogService>();
        services.AddSingleton<IContainerCatalogService, NamedPipeContainerCatalogService>();
        services.AddSingleton<ISessionService, NamedPipeSessionService>();
        services.AddSingleton<INetworkingService, NamedPipeNetworkingService>();
        services.AddSingleton<NamedPipeDashboardSnapshotService>();
        services.AddSingleton<ShellViewModel>();
        services.AddTransient<HomeViewModel>();
        services.AddTransient<ImagesViewModel>();
        services.AddTransient<ContainersViewModel>();
        services.AddTransient<SessionViewModel>();
        services.AddTransient<NetworkingViewModel>();
        services.AddTransient<RunWizardViewModel>();

        return services.BuildServiceProvider();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        LogStartupFailure(e.Exception);
        e.Handled = true;
    }

    private void OnCurrentDomainUnhandledException(object? sender, System.UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            LogStartupFailure(exception);
        }
    }

    private static void LogStartupFailure(Exception exception)
    {
        Directory.CreateDirectory(DiagnosticsDirectory);
        File.WriteAllText(Path.Combine(DiagnosticsDirectory, "startup-error.log"), exception.ToString());
    }

    internal static void TraceStartup(string message)
    {
        Directory.CreateDirectory(DiagnosticsDirectory);
        File.AppendAllText(
            Path.Combine(DiagnosticsDirectory, "startup-trace.log"),
            $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
    }
}
