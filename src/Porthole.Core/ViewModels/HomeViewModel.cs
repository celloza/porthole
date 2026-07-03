using CommunityToolkit.Mvvm.ComponentModel;
using Porthole.Core.Models;
using Porthole.Core.Services;

namespace Porthole.Core.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private readonly IWslcService _wslcService;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

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
    private string containerSummaryText = "0 running / 0 stopped";

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
        var report = _wslcService.GetMissingComponents();
        IsPrereleaseUpdateRequired = !report.IsReady;
        MissingComponentsMessage = report.Summary;

        CpuUsageText = snapshot.CpuUsageText;
        MemoryUsageText = snapshot.MemoryUsageText;
        ContainerSummaryText = snapshot.ContainerSummaryText;
        SessionStatus = report.IsReady
            ? snapshot.SessionStatus
            : "WSL Containers is not ready yet. Resolve prerequisites before opening Images or Containers.";
    }
}