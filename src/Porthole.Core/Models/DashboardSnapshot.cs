namespace Porthole.Core.Models;

public sealed record DashboardSnapshot(
    string CpuUsageText,
    string MemoryUsageText,
    string ContainerSummaryText,
    string SessionStatus);