namespace Porthole.Core.Models;

public sealed record SessionSummary(
    string Name,
    string StoragePath,
    bool IsActive)
{
    public bool IsNotActive => !IsActive;
}
