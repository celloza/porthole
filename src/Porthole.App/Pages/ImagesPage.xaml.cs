using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Porthole.Core.Models;
using Porthole.Core.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Porthole_App.Pages;

public sealed partial class ImagesPage : Page
{
    private const string SkipPruneConfirmationSettingKey = "Images.SkipPruneConfirmation";

    private bool _initialized;
    private CancellationTokenSource? _subscriptionCancellationSource;
    private Task? _subscriptionTask;

    public ImagesViewModel ViewModel { get; }

    public ImagesPage()
    {
        ViewModel = (ImagesViewModel)App.Services.GetService(typeof(ImagesViewModel))!;
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
                await ViewModel.SubscribeAsync(async images =>
                {
                    await EnqueueAsync(() =>
                    {
                        ViewModel.ApplyInventoryUpdate(images);
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
                    ViewModel.ActionStatus = "Image subscription disconnected. Reconnecting...";
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
            completion.SetException(new InvalidOperationException("Failed to dispatch image update to the UI thread."));
        }

        return completion.Task;
    }

    private async void PruneImagesButton_Click(object sender, RoutedEventArgs e)
    {
        bool shouldProceed = await ConfirmPruneImagesAsync();
        if (!shouldProceed)
        {
            return;
        }

        try
        {
            await ViewModel.PruneImagesCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            ViewModel.ActionStatus = $"Image prune failed: {ex.Message}";
        }
    }

    private async Task<bool> ConfirmPruneImagesAsync()
    {
        if (ShouldSkipPruneConfirmation())
        {
            return true;
        }

        var doNotAskAgainCheckBox = new CheckBox
        {
            Content = "Do not ask me again",
            Margin = new Thickness(0, 8, 0, 0),
        };

        var dialogContent = new StackPanel
        {
            Spacing = 8,
        };

        dialogContent.Children.Add(new TextBlock
        {
            Text = "Prune removes unused images from the local cache. Continue?",
            TextWrapping = TextWrapping.Wrap,
        });
        dialogContent.Children.Add(doNotAskAgainCheckBox);

        var dialog = new ContentDialog
        {
            Title = "Prune unused images",
            PrimaryButtonText = "Prune",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            Content = dialogContent,
            XamlRoot = XamlRoot,
        };

        ContentDialogResult result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return false;
        }

        if (doNotAskAgainCheckBox.IsChecked == true)
        {
            ApplicationData.Current.LocalSettings.Values[SkipPruneConfirmationSettingKey] = true;
        }

        return true;
    }

    private static bool ShouldSkipPruneConfirmation()
    {
        object? value = ApplicationData.Current.LocalSettings.Values[SkipPruneConfirmationSettingKey];
        return value is bool boolValue && boolValue;
    }

    private void ItemCopyIdButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el && el.Tag is string sha)
        {
            var dataPackage = new DataPackage();
            dataPackage.SetText(sha);
            Clipboard.SetContent(dataPackage);
        }
    }

    private Visibility GetDetailVisibility(ImageSummary? image) =>
        image is not null ? Visibility.Visible : Visibility.Collapsed;

    private Visibility GetEmptyStateVisibility(ImageSummary? image) =>
        image is null ? Visibility.Visible : Visibility.Collapsed;
}