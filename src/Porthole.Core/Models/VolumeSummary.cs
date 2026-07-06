namespace Porthole.Core.Models;

public sealed record VolumeSummary(
    string Name,
    string Driver,
    string MountPoint,
    string? HostPath,
    string SizeLabel,
    bool IsInUse,
    bool IsBindMount = false,
    bool IsReadOnly = false,
    string? ThroughputClass = null)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Name)
        ? (string.IsNullOrWhiteSpace(HostPath) ? MountPoint : HostPath)
        : Name;

    public string DriverDisplay => string.IsNullOrWhiteSpace(Driver) ? "local" : Driver;

    public string InUseLabel => IsInUse ? "In use" : "Unused";

    public string InUseGlyph => IsInUse ? "\uE768" : "\uE71A";

    public string TypeLabel => IsBindMount ? "Bind mount" : "Named volume";

    public string AccessModeLabel => IsReadOnly ? "Read-only" : "Read-write";

    public string ThroughputClassDisplay => string.IsNullOrWhiteSpace(ThroughputClass)
        ? (IsBindMount ? "virtiofs" : "session local")
        : ThroughputClass;

    public string SourceDisplay => string.IsNullOrWhiteSpace(HostPath) ? Name : HostPath;

    public string PathSummary => string.IsNullOrWhiteSpace(HostPath)
        ? MountPoint
        : $"{HostPath} -> {MountPoint}";

    public string MountString => string.IsNullOrWhiteSpace(HostPath)
        ? $"{Name}:{MountPoint}"
        : $"{HostPath}:{MountPoint}";

    public bool CanDelete => !IsBindMount && !IsInUse && !string.IsNullOrWhiteSpace(Name);

    public string DeleteToolTip => CanDelete
        ? "Delete volume"
        : IsBindMount
            ? "Bind mounts are removed by editing the container configuration."
            : "Only unused named volumes can be deleted.";
}
