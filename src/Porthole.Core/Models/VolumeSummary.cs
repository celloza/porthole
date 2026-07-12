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
    string? ThroughputClass = null,
    string? SessionMountPoint = null,
    DateTimeOffset? CreatedAtUtc = null)
{
    public string DisplayName => string.IsNullOrWhiteSpace(HostPath)
        ? (string.IsNullOrWhiteSpace(Name) ? MountPoint : Name)
        : HostPath;

    public string DriverDisplay => string.IsNullOrWhiteSpace(Driver) ? "local" : Driver;

    public string InUseLabel => IsInUse ? "In use" : "Unused";

    public string InUseGlyph => IsInUse ? "\uE768" : "\uE71A";

    public string TypeLabel => IsBindMount ? "Bind mount" : "Named volume";

    public string AccessModeLabel => IsReadOnly ? "Read-only" : "Read-write";

    public string ThroughputClassDisplay => string.IsNullOrWhiteSpace(ThroughputClass)
        ? (IsBindMount ? "virtiofs" : "session local")
        : ThroughputClass;

    public string SourceDisplay => string.IsNullOrWhiteSpace(HostPath) ? Name : HostPath;

    public string PathSummary
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(HostPath))
            {
                return string.IsNullOrWhiteSpace(MountPoint)
                    ? HostPath
                    : $"{HostPath} -> {MountPoint}";
            }

            if (!string.IsNullOrWhiteSpace(MountPoint))
            {
                return MountPoint;
            }

            return string.IsNullOrWhiteSpace(SessionMountPoint) ? string.Empty : SessionMountPoint;
        }
    }

    public string MountString
    {
        get
        {
            if (string.IsNullOrWhiteSpace(MountPoint))
            {
                return string.Empty;
            }

            return string.IsNullOrWhiteSpace(HostPath)
                ? $"{Name}:{MountPoint}"
                : $"{HostPath}:{MountPoint}";
        }
    }

    public string CopyMountToolTip => string.IsNullOrWhiteSpace(MountString)
        ? "No mount string available until the volume is attached to a container target path."
        : "Copy mount string";

    public bool CanCopyMountString => !string.IsNullOrWhiteSpace(MountString);

    public bool CanDelete => !IsBindMount && !IsInUse && !string.IsNullOrWhiteSpace(Name);

    public string DeleteToolTip => CanDelete
        ? "Delete volume"
        : IsBindMount
            ? "Bind mounts are removed by editing the container configuration."
            : "Only unused named volumes can be deleted.";
}
