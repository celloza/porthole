namespace Porthole.Core.Models;

public sealed record DashboardSnapshot(
    string CpuUsageText,
    string MemoryUsageText,
    string ContainerSummaryText,
    string SessionStatus)
{
    /// <summary>Raw CPU utilisation percentage (0-100). Defaults to 0 when not available.</summary>
    public double CpuPercent { get; init; }

    /// <summary>Raw memory utilisation percentage (0-100). Defaults to 0 when not available.</summary>
    public double MemoryPercent { get; init; }
}