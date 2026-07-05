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

    // Networking
    GetNetworkingSnapshot = 30,
    SetNetworkMode = 31,
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
    NetworkMode? NetworkMode = null);

public sealed record ImageCatalogResponse(
    ImageCatalogMessageKind Kind,
    string? Message = null,
    DashboardSnapshot? Snapshot = null,
    IReadOnlyList<ContainerSummary>? Containers = null,
    IReadOnlyList<PodSummary>? Pods = null,
    IReadOnlyList<ImageSummary>? Images = null,
    ImagePullProgress? Progress = null,
    IReadOnlyList<SessionSummary>? Sessions = null,
    NetworkingSnapshot? NetworkingSnapshot = null);