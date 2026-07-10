using Porthole.Core.Models;

namespace Porthole.Core.Services.NamedPipe;

public enum ImageCatalogOperation
{
    List = 0,
    Pull = 1,
    Prune = 2,
    Tag = 3,
    Delete = 4,
    DashboardSnapshot = 10,
    SubscribeDashboard = 11,
    ListContainers = 12,
    StartContainer = 13,
    StopContainer = 14,
    RemoveContainer = 15,
    ListPods = 16,
    SubscribeContainers = 17,
    SubscribeImages = 18,

    // Session management
    ListSessions = 20,
    CreateSession = 21,
    DeleteSession = 22,
    SetActiveSession = 23,
    GetActiveSession = 24,

    // Container creation
    CreateContainer = 25,

    // Networking
    GetNetworkingSnapshot = 30,
    SetNetworkMode = 31,

    // Session quick controls (tray flyout)
    PauseSession = 26,
    ResumeSession = 27,
    TerminateSession = 28,
    GetTraySnapshot = 29,

    // Volume management
    ListVolumes = 40,
    CreateVolume = 41,
    DeleteVolume = 42,
    PruneVolumes = 43,
}

public enum ImageCatalogMessageKind
{
    Request,
    Progress,
    Response,
    Error,
}

public sealed record ImageCatalogRequest(
    ImageCatalogOperation Operation,
    string? ImageReference = null,
    string? NewTag = null,
    string? ContainerReference = null,
    string? SessionName = null,
    NetworkMode? NetworkMode = null,
    ContainerConfig? ContainerConfig = null,
    string? VolumeName = null);

public sealed record ImageCatalogResponse(
    ImageCatalogMessageKind Kind,
    string? Message = null,
    DashboardSnapshot? Snapshot = null,
    IReadOnlyList<ContainerSummary>? Containers = null,
    IReadOnlyList<PodSummary>? Pods = null,
    IReadOnlyList<ImageSummary>? Images = null,
    ImagePullProgress? Progress = null,
    IReadOnlyList<SessionSummary>? Sessions = null,
    NetworkingSnapshot? NetworkingSnapshot = null,
    IReadOnlyList<VolumeSummary>? Volumes = null,
    IReadOnlyList<SessionSnapshot>? TraySnapshots = null);