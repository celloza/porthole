using Porthole.Core.Models;

namespace Porthole.Core.Services;

public interface ISessionService
{
    Task<IReadOnlyList<SessionSummary>> ListSessionsAsync(CancellationToken cancellationToken = default);
    Task CreateSessionAsync(string name, CancellationToken cancellationToken = default);
    Task DeleteSessionAsync(string name, CancellationToken cancellationToken = default);
    Task SetActiveSessionAsync(string name, CancellationToken cancellationToken = default);
    Task<string> GetActiveSessionNameAsync(CancellationToken cancellationToken = default);
}
