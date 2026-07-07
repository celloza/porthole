using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Porthole.Core.Services.NamedPipe;
using Porthole.Core.ViewModels;
using System.Diagnostics;
using Windows.Foundation;
using Windows.UI;

namespace Porthole_App.Pages;

public sealed partial class HomePage : Page
{
    private static readonly SolidColorBrush ConnectedBrush = new(Colors.ForestGreen);
    private static readonly SolidColorBrush ConnectingBrush = new(Colors.Goldenrod);
    private static readonly SolidColorBrush FailedBrush = new(Colors.IndianRed);

    private readonly DispatcherTimer _refreshTimer;
    private readonly NamedPipeDashboardSnapshotService _dashboardSubscriptionService;
    private CancellationTokenSource? _refreshCancellationSource;
    private CancellationTokenSource? _subscriptionCancellationSource;
    private Task? _subscriptionTask;

    // Sparkline graph elements
    private Polyline? _cpuLine;
    private Polygon? _cpuFill;
    private readonly Line[] _cpuGridLines = new Line[3];
    private Polyline? _memLine;
    private Polygon? _memFill;
    private readonly Line[] _memGridLines = new Line[3];

    public HomeViewModel ViewModel { get; }

    public HomePage()
    {
        ViewModel = (HomeViewModel)App.Services.GetService(typeof(HomeViewModel))!;
        _dashboardSubscriptionService = (NamedPipeDashboardSnapshotService)App.Services.GetService(typeof(NamedPipeDashboardSnapshotService))!;
        InitializeComponent();
        InitializeGraphCanvases();

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

        // Subscribe to history updates to redraw graphs
        ViewModel.HistoryUpdated += OnHistoryUpdated;

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

        // Unsubscribe from history updates
        ViewModel.HistoryUpdated -= OnHistoryUpdated;
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

    private void OnHistoryUpdated(object? sender, EventArgs e)
    {
        // Redraw graphs when history updates (new samples received from tray)
        RedrawGraphs();
    }

    public Brush GetSessionStatusBrush(string? sessionStatus, bool isLoading)
    {
        if (isLoading)
        {
            return ConnectingBrush;
        }

        string normalizedStatus = (sessionStatus ?? string.Empty).Trim();
        if (normalizedStatus.Length == 0)
        {
            return ConnectingBrush;
        }

        if (ContainsAny(normalizedStatus, "checking", "reconnecting", "connecting", "starting", "loading"))
        {
            return ConnectingBrush;
        }

        if (ContainsAny(normalizedStatus, "failed", "not ready", "timed out", "unavailable", "error", "disconnected", "incomplete"))
        {
            return FailedBrush;
        }

        if (normalizedStatus.Contains("connected", StringComparison.OrdinalIgnoreCase))
        {
            return ConnectedBrush;
        }

        return ConnectingBrush;
    }

    private async void RunWslUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/k wsl --update --pre-release",
                UseShellExecute = true,
            });

            if (process is null)
            {
                throw new InvalidOperationException("The terminal process could not be started.");
            }
        }
        catch (Exception ex)
        {
            var dialog = new ContentDialog
            {
                Title = "Failed to launch update",
                Content = new TextBlock
                {
                    Text = $"Could not open a terminal to run 'wsl --update --pre-release'.{Environment.NewLine}{Environment.NewLine}{ex.Message}{Environment.NewLine}{Environment.NewLine}Try running the command manually in a terminal window.",
                    TextWrapping = Microsoft.UI.Xaml.TextWrapping.WrapWholeWords,
                },
                CloseButtonText = "OK",
                XamlRoot = XamlRoot,
            };

            await dialog.ShowAsync();
        }
    }

    private static bool ContainsAny(string value, params string[] terms)
    {
        foreach (string term in terms)
        {
            if (value.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // -------------------------------------------------------------------------
    // Sparkline graphs
    // -------------------------------------------------------------------------

    private void InitializeGraphCanvases()
    {
        (_cpuLine, _cpuFill) = SetupGraphCanvas(
            CpuCanvas, _cpuGridLines,
            Color.FromArgb(255, 75, 158, 255));

        (_memLine, _memFill) = SetupGraphCanvas(
            MemCanvas, _memGridLines,
            Color.FromArgb(255, 75, 193, 166));

        CpuCanvas.SizeChanged += (_, _) => RedrawGraphs();
        MemCanvas.SizeChanged += (_, _) => RedrawGraphs();
    }

    private static (Polyline line, Polygon fill) SetupGraphCanvas(
        Canvas canvas, Line[] gridLines, Color lineColor)
    {
        var gridBrush = new SolidColorBrush(Color.FromArgb(20, 128, 128, 128));
        for (int i = 0; i < gridLines.Length; i++)
        {
            gridLines[i] = new Line { Stroke = gridBrush, StrokeThickness = 1 };
            canvas.Children.Add(gridLines[i]);
        }

        var fill = new Polygon
        {
            Fill = new SolidColorBrush(Color.FromArgb(35, lineColor.R, lineColor.G, lineColor.B)),
        };
        canvas.Children.Add(fill);

        var line = new Polyline
        {
            Stroke = new SolidColorBrush(lineColor),
            StrokeThickness = 1.5,
            StrokeLineJoin = PenLineJoin.Round,
        };
        canvas.Children.Add(line);

        return (line, fill);
    }

    private void RedrawGraphs()
    {
        if (_cpuLine is not null && _cpuFill is not null)
            RedrawGraph(CpuCanvas, _cpuGridLines, _cpuFill, _cpuLine, ViewModel.CpuHistory);
        if (_memLine is not null && _memFill is not null)
            RedrawGraph(MemCanvas, _memGridLines, _memFill, _memLine, ViewModel.MemoryHistory);
    }

    private static void RedrawGraph(
        Canvas canvas, Line[] gridLines, Polygon fill, Polyline line,
        IReadOnlyList<double> history)
    {
        double w = canvas.ActualWidth;
        double h = canvas.ActualHeight;
        if (w <= 0 || h <= 0) return;

        // Horizontal reference lines at 25 %, 50 %, 75 %
        double[] levels = { 0.75, 0.5, 0.25 };
        for (int i = 0; i < gridLines.Length; i++)
        {
            double y = levels[i] * h;
            gridLines[i].X2 = w;
            gridLines[i].Y1 = y;
            gridLines[i].Y2 = y;
        }

        int n = history.Count;
        if (n == 0)
        {
            fill.Points.Clear();
            line.Points.Clear();
            return;
        }

        // Build line points (oldest left → newest right)
        var pts = new List<Point>(n);
        for (int i = 0; i < n; i++)
        {
            double x = n <= 1 ? w : (double)i / (n - 1) * w;
            double y = h - history[i] / 100.0 * h;
            pts.Add(new Point(x, Math.Clamp(y, 0, h)));
        }

        line.Points.Clear();
        foreach (Point p in pts) line.Points.Add(p);

        // Fill polygon: line path + close down to the baseline
        fill.Points.Clear();
        foreach (Point p in pts) fill.Points.Add(p);
        fill.Points.Add(new Point(pts[^1].X, h));
        fill.Points.Add(new Point(pts[0].X, h));
    }
}
