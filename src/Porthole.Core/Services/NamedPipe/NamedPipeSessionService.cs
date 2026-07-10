using Porthole.Core.Models;

namespace Porthole.Core.Services.NamedPipe;

public sealed class NamedPipeSessionService : ISessionService
{
    public async Task<IReadOnlyList<SessionSummary>> ListSessionsAsync(CancellationToken cancellationToken = default)
    {
        var response = await NamedPipeImageCatalogService.SendRequestAsync(
            new ImageCatalogRequest(ImageCatalogOperation.ListSessions),
            progress: null,
            cancellationToken);

        return response.Sessions ?? [];
    }

    public async Task CreateSessionAsync(string name, CancellationToken cancellationToken = default)
    {
        await NamedPipeImageCatalogService.SendRequestAsync(
            new ImageCatalogRequest(ImageCatalogOperation.CreateSession, SessionName: name),
            progress: null,
            cancellationToken);
    }

    public async Task DeleteSessionAsync(string name, CancellationToken cancellationToken = default)
    {
        await NamedPipeImageCatalogService.SendRequestAsync(
            new ImageCatalogRequest(ImageCatalogOperation.DeleteSession, SessionName: name),
            progress: null,
            cancellationToken);
    }

    public async Task SetActiveSessionAsync(string name, CancellationToken cancellationToken = default)
    {
        await NamedPipeImageCatalogService.SendRequestAsync(
            new ImageCatalogRequest(ImageCatalogOperation.SetActiveSession, SessionName: name),
            progress: null,
            cancellationToken);
    }

    public async Task<string> GetActiveSessionNameAsync(CancellationToken cancellationToken = default)
    {
        var response = await NamedPipeImageCatalogService.SendRequestAsync(
            new ImageCatalogRequest(ImageCatalogOperation.GetActiveSession),
            progress: null,
            cancellationToken);

        return response.Message ?? string.Empty;
    }

    public async Task PauseSessionAsync(string name, CancellationToken cancellationToken = default)
    {
        await NamedPipeImageCatalogService.SendRequestAsync(
            new ImageCatalogRequest(ImageCatalogOperation.PauseSession, SessionName: name),
            progress: null,
            cancellationToken);
    }

    public async Task ResumeSessionAsync(string name, CancellationToken cancellationToken = default)
    {
        await NamedPipeImageCatalogService.SendRequestAsync(
            new ImageCatalogRequest(ImageCatalogOperation.ResumeSession, SessionName: name),
            progress: null,
            cancellationToken);
    }

    public async Task TerminateSessionAsync(string name, CancellationToken cancellationToken = default)
    {
        await NamedPipeImageCatalogService.SendRequestAsync(
            new ImageCatalogRequest(ImageCatalogOperation.TerminateSession, SessionName: name),
            progress: null,
            cancellationToken);
    }

    public async Task<IReadOnlyList<SessionSnapshot>> GetTraySnapshotAsync(CancellationToken cancellationToken = default)
    {
        var response = await NamedPipeImageCatalogService.SendRequestAsync(
            new ImageCatalogRequest(ImageCatalogOperation.GetTraySnapshot),
            progress: null,
            cancellationToken);

        return response.TraySnapshots ?? [];
    }
}
