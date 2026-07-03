using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Porthole.Core.Services.NamedPipe;
using Porthole.Core.ViewModels;

namespace Porthole_App.Pages;

public sealed partial class HomePage : Page
{
    private readonly DispatcherTimer _refreshTimer;
    private readonly NamedPipeDashboardSnapshotService _dashboardSubscriptionService;
    private CancellationTokenSource? _refreshCancellationSource;
    private CancellationTokenSource? _subscriptionCancellationSource;
    private Task? _subscriptionTask;

    public HomeViewModel ViewModel { get; }

    public HomePage()
    {
        ViewModel = (HomeViewModel)App.Services.GetService(typeof(HomeViewModel))!;
        _dashboardSubscriptionService = (NamedPipeDashboardSnapshotService)App.Services.GetService(typeof(NamedPipeDashboardSnapshotService))!;
        InitializeComponent();

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30),
        };

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        _refreshTimer.Tick += OnRefreshTimerTick;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await RefreshDashboardAsync();

        _subscriptionCancellationSource?.Cancel();
        _subscriptionCancellationSource?.Dispose();
        _subscriptionCancellationSource = new CancellationTokenSource();
        _subscriptionTask = RunSubscriptionLoopAsync(_subscriptionCancellationSource.Token);

        if (!_refreshTimer.IsEnabled)
        {
            _refreshTimer.Start();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _refreshTimer.Stop();
        _refreshCancellationSource?.Cancel();
        _refreshCancellationSource?.Dispose();
        _refreshCancellationSource = null;

        _subscriptionCancellationSource?.Cancel();
        _subscriptionCancellationSource?.Dispose();
        _subscriptionCancellationSource = null;
    }

    private async void OnRefreshTimerTick(object? sender, object e)
    {
        await RefreshDashboardAsync();
    }

    private async Task RefreshDashboardAsync()
    {
        _refreshCancellationSource?.Cancel();
        _refreshCancellationSource?.Dispose();
        _refreshCancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        try
        {
            await ViewModel.InitializeAsync(_refreshCancellationSource.Token);
        }
        catch (OperationCanceledException)
        {
            ViewModel.SessionStatus = "Dashboard refresh timed out. Tray telemetry did not respond in time.";
        }
    }

    private async Task RunSubscriptionLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _dashboardSubscriptionService.SubscribeAsync(async snapshot =>
                {
                    await EnqueueAsync(() =>
                    {
                        ViewModel.ApplySnapshot(snapshot);
                        ViewModel.IsLoading = false;
                    });
                }, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                await EnqueueAsync(() =>
                {
                    ViewModel.SessionStatus = "Dashboard subscription disconnected. Reconnecting...";
                });

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private Task EnqueueAsync(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        if (!DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    action();
                    completion.SetResult();
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
            }))
        {
            completion.SetException(new InvalidOperationException("Failed to dispatch dashboard update to the UI thread."));
        }

        return completion.Task;
    }
}
