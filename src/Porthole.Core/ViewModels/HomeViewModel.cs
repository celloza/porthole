using CommunityToolkit.Mvvm.ComponentModel;
using Porthole.Core.Models;
using Porthole.Core.Services;

namespace Porthole.Core.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    public const int GraphHistoryCapacity = 60;

    private readonly IWslcService _wslcService;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly Queue<double> _cpuHistory = new();
    private readonly Queue<double> _memHistory = new();

    public event EventHandler? HistoryUpdated;

    public IReadOnlyList<double> CpuHistory { get; private set; } = [];
    public IReadOnlyList<double> MemoryHistory { get; private set; } = [];

    [ObservableProperty]
    private bool isLoading = true;

    [ObservableProperty]
    private bool isPrereleaseUpdateRequired;

    [ObservableProperty]
    private string missingComponentsMessage = string.Empty;

    [ObservableProperty]
    private string sessionStatus = "Checking WSL Containers prerequisites...";

    [ObservableProperty]
    private string cpuUsageText = "Pending";

    [ObservableProperty]
    private string memoryUsageText = "Pending";

    [ObservableProperty]
    private string containerSummaryText = "0 running";

    public HomeViewModel(IWslcService wslcService)
    {
        _wslcService = wslcService;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _refreshGate.WaitAsync(cancellationToken);

        try
        {
            IsLoading = true;

            var report = _wslcService.GetMissingComponents();
            IsPrereleaseUpdateRequired = !report.IsReady;
            MissingComponentsMessage = report.Summary;

            var snapshot = await Task.Run(
                async () => await _wslcService.GetDashboardSnapshotAsync(cancellationToken),
                cancellationToken);

            ApplySnapshot(snapshot);
        }
        finally
        {
            IsLoading = false;
            _refreshGate.Release();
        }
    }

    public void ApplySnapshot(DashboardSnapshot snapshot)
    {
        // DEBUG: Log received snapshot values
        System.Diagnostics.Debug.WriteLine($"[HomeViewModel.ApplySnapshot] CpuPercent={snapshot.CpuPercent}, MemoryPercent={snapshot.MemoryPercent}");

        var report = _wslcService.GetMissingComponents();
        IsPrereleaseUpdateRequired = !report.IsReady;
        MissingComponentsMessage = report.Summary;

        CpuUsageText = snapshot.CpuUsageText;
        MemoryUsageText = snapshot.MemoryUsageText;
        ContainerSummaryText = snapshot.ContainerSummaryText;
        SessionStatus = report.IsReady
            ? snapshot.SessionStatus
            : "WSL Containers is not ready yet. Resolve prerequisites before opening Images or Containers.";

        PushToHistory(snapshot.CpuPercent, snapshot.MemoryPercent);
    }

    private void PushToHistory(double cpuPercent, double memPercent)
    {
        if (_cpuHistory.Count >= GraphHistoryCapacity) _cpuHistory.Dequeue();
        _cpuHistory.Enqueue(Math.Clamp(cpuPercent, 0, 100));
        if (_memHistory.Count >= GraphHistoryCapacity) _memHistory.Dequeue();
        _memHistory.Enqueue(Math.Clamp(memPercent, 0, 100));
        CpuHistory = _cpuHistory.ToArray();
        MemoryHistory = _memHistory.ToArray();
        HistoryUpdated?.Invoke(this, EventArgs.Empty);
    }
}