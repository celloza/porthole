namespace Porthole.Core.Models;

public sealed record SessionSnapshot(
    string Name,
    bool IsActive,
    string Status,
    string CpuUsage,
    string MemoryUsage);
