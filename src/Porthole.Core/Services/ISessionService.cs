using Porthole.Core.Models;

namespace Porthole.Core.Services;

public interface ISessionService
{
    Task<IReadOnlyList<SessionSummary>> ListSessionsAsync(CancellationToken cancellationToken = default);
    Task CreateSessionAsync(string name, CancellationToken cancellationToken = default);
    Task DeleteSessionAsync(string name, CancellationToken cancellationToken = default);
    Task SetActiveSessionAsync(string name, CancellationToken cancellationToken = default);
    Task<string> GetActiveSessionNameAsync(CancellationToken cancellationToken = default);
    Task PauseSessionAsync(string name, CancellationToken cancellationToken = default);
    Task ResumeSessionAsync(string name, CancellationToken cancellationToken = default);
    Task TerminateSessionAsync(string name, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SessionSnapshot>> GetTraySnapshotAsync(CancellationToken cancellationToken = default);
}
