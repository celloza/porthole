using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Porthole.Core.ViewModels;

namespace Porthole_App.Pages;

public sealed partial class ContainersPage : Page
{
    private bool _initialized;
    private CancellationTokenSource? _subscriptionCancellationSource;
    private Task? _subscriptionTask;

    public ContainersViewModel ViewModel { get; }

    public ContainersPage()
    {
        ViewModel = (ContainersViewModel)App.Services.GetService(typeof(ContainersViewModel))!;
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_initialized)
        {
            _initialized = true;
            await ViewModel.InitializeAsync();
        }

        _subscriptionCancellationSource?.Cancel();
        _subscriptionCancellationSource?.Dispose();
        _subscriptionCancellationSource = new CancellationTokenSource();
        _subscriptionTask = RunSubscriptionLoopAsync(_subscriptionCancellationSource.Token);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _subscriptionCancellationSource?.Cancel();
        _subscriptionCancellationSource?.Dispose();
        _subscriptionCancellationSource = null;
    }

    private async Task RunSubscriptionLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ViewModel.SubscribeAsync(async (containers, pods) =>
                {
                    await EnqueueAsync(() =>
                    {
                        ViewModel.ApplyCatalogUpdate(containers, pods);
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
                    ViewModel.ActionStatus = "Container subscription disconnected. Reconnecting...";
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
            completion.SetException(new InvalidOperationException("Failed to dispatch container update to the UI thread."));
        }

        return completion.Task;
    }
}