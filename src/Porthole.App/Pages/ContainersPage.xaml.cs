using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Porthole.Core.Models;
using Porthole.Core.ViewModels;
using Windows.ApplicationModel.DataTransfer;

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
        ViewModel.SessionChanged += OnSessionChanged;
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        // Restart the subscription loop when the session changes
        _subscriptionCancellationSource?.Cancel();
        _subscriptionCancellationSource?.Dispose();
        _subscriptionCancellationSource = new CancellationTokenSource();
        _subscriptionTask = RunSubscriptionLoopAsync(_subscriptionCancellationSource.Token);
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

    private void ItemCopyIdButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el && el.Tag is string id)
        {
            var dataPackage = new DataPackage();
            dataPackage.SetText(id);
            Clipboard.SetContent(dataPackage);
        }
    }

    private Visibility GetDetailVisibility(ContainerSummary? container) =>
        container is not null ? Visibility.Visible : Visibility.Collapsed;

    private Visibility GetEmptyStateVisibility(ContainerSummary? container) =>
        container is null ? Visibility.Visible : Visibility.Collapsed;
}