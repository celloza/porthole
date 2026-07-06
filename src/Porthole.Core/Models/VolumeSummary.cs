namespace Porthole.Core.Models;

public sealed record VolumeSummary(
    string Name,
    string Driver,
    string MountPoint,
    string? HostPath,
    string SizeLabel,
    bool IsInUse)
{
    public string DriverDisplay => string.IsNullOrWhiteSpace(Driver) ? "local" : Driver;

    public string InUseLabel => IsInUse ? "In use" : "Unused";

    public string InUseGlyph => IsInUse ? "\uE768" : "\uE71A";
}
