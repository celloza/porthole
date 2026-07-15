using Porthole.Core.Models;

namespace Porthole.Core.Services.NamedPipe;

public sealed class NamedPipeSessionService : ISessionService
{
    private CancellationTokenSource? _watchCancellationTokenSource;
    private Task? _watchTask;
    private IReadOnlyList<SessionSummary> _lastKnownSessions = [];

    public event EventHandler? SessionsChanged;

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

        OnSessionsChanged();
    }

    public async Task DeleteSessionAsync(string name, CancellationToken cancellationToken = default)
    {
        await NamedPipeImageCatalogService.SendRequestAsync(
            new ImageCatalogRequest(ImageCatalogOperation.DeleteSession, SessionName: name),
            progress: null,
            cancellationToken);

        OnSessionsChanged();
    }

    public async Task SetActiveSessionAsync(string name, CancellationToken cancellationToken = default)
    {
        await NamedPipeImageCatalogService.SendRequestAsync(
            new ImageCatalogRequest(ImageCatalogOperation.SetActiveSession, SessionName: name),
            progress: null,
            cancellationToken);

        OnSessionsChanged();
    }

    public async Task<string> GetActiveSessionNameAsync(CancellationToken cancellationToken = default)
    {
        var response = await NamedPipeImageCatalogService.SendRequestAsync(
            new ImageCatalogRequest(ImageCatalogOperation.GetActiveSession),
            progress: null,
            cancellationToken);

        return response.Message ?? string.Empty;
    }

    public void StartWatchingForChanges(CancellationToken cancellationToken = default)
    {
        if (_watchTask is not null && !_watchTask.IsCompleted)
            return; // Already watching

        _watchCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _watchTask = WatchForChangesAsync(_watchCancellationTokenSource.Token);
    }

    public void StopWatchingForChanges()
    {
        _watchCancellationTokenSource?.Cancel();
        _watchCancellationTokenSource?.Dispose();
        _watchCancellationTokenSource = null;
    }

    private async Task WatchForChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(2000, cancellationToken); // Check every 2 seconds

                try
                {
                    var currentSessions = await ListSessionsAsync(cancellationToken);

                    // Check if sessions list changed
                    if (HaveSessionsChanged(currentSessions))
                    {
                        _lastKnownSessions = currentSessions;
                        OnSessionsChanged();
                    }
                }
                catch
                {
                    // Silently ignore errors during watch (backend might be unavailable)
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when watch is stopped
        }
    }

    private bool HaveSessionsChanged(IReadOnlyList<SessionSummary> newSessions)
    {
        if (_lastKnownSessions.Count != newSessions.Count)
            return true;

        // Check if any session names changed
        var lastNames = _lastKnownSessions.Select(s => s.Name).OrderBy(n => n).ToList();
        var newNames = newSessions.Select(s => s.Name).OrderBy(n => n).ToList();

        if (!lastNames.SequenceEqual(newNames))
            return true;

        // Also check if the active session changed (e.g. switched via the tray flyout)
        string? lastActive = _lastKnownSessions.FirstOrDefault(s => s.IsActive)?.Name;
        string? newActive = newSessions.FirstOrDefault(s => s.IsActive)?.Name;
        return !string.Equals(lastActive, newActive, StringComparison.OrdinalIgnoreCase);
    }

    private void OnSessionsChanged()
    {
        SessionsChanged?.Invoke(this, EventArgs.Empty);
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
