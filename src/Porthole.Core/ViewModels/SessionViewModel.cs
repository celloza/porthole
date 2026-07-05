using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Porthole.Core.Models;
using Porthole.Core.Services;

namespace Porthole.Core.ViewModels;

public partial class SessionViewModel : ObservableObject
{
    private readonly ISessionService _sessionService;

    public ObservableCollection<SessionSummary> Sessions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    private string newSessionName = string.Empty;

    [ObservableProperty]
    private string activeSessionName = string.Empty;

    [ObservableProperty]
    private string statusMessage = "Load sessions to get started.";

    [ObservableProperty]
    private bool isLoading;

    public bool CanCreate =>
        !string.IsNullOrWhiteSpace(NewSessionName)
        && !Sessions.Any(s => string.Equals(s.Name, NewSessionName, StringComparison.OrdinalIgnoreCase));

    public SessionViewModel(ISessionService sessionService)
    {
        _sessionService = sessionService;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await LoadSessionsAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task LoadSessionsAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        StatusMessage = "Loading sessions...";

        try
        {
            var sessions = await _sessionService.ListSessionsAsync(cancellationToken);
            string activeName = await _sessionService.GetActiveSessionNameAsync(cancellationToken);

            Sessions.Clear();
            foreach (var session in sessions)
            {
                Sessions.Add(session);
            }

            ActiveSessionName = activeName;
            StatusMessage = Sessions.Count == 0
                ? "No sessions found."
                : $"{Sessions.Count} session{(Sessions.Count == 1 ? string.Empty : "s")} loaded.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load sessions: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateSessionAsync(CancellationToken cancellationToken = default)
    {
        string name = NewSessionName.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        IsLoading = true;
        StatusMessage = $"Creating session '{name}'...";

        try
        {
            await _sessionService.CreateSessionAsync(name, cancellationToken);
            NewSessionName = string.Empty;
            StatusMessage = $"Session '{name}' created.";
            await LoadSessionsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to create session '{name}': {ex.Message}";
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SwitchSessionAsync(SessionSummary session, CancellationToken cancellationToken = default)
    {
        if (string.Equals(session.Name, ActiveSessionName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        IsLoading = true;
        StatusMessage = $"Switching to session '{session.Name}'...";

        try
        {
            await _sessionService.SetActiveSessionAsync(session.Name, cancellationToken);
            ActiveSessionName = session.Name;
            StatusMessage = $"Active session is now '{session.Name}'.";
            await LoadSessionsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to switch to session '{session.Name}': {ex.Message}";
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task DeleteSessionAsync(SessionSummary session, CancellationToken cancellationToken = default)
    {
        if (string.Equals(session.Name, ActiveSessionName, StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "Cannot delete the active session. Switch to another session first.";
            return;
        }

        IsLoading = true;
        StatusMessage = $"Deleting session '{session.Name}'...";

        try
        {
            await _sessionService.DeleteSessionAsync(session.Name, cancellationToken);
            StatusMessage = $"Session '{session.Name}' deleted.";
            await LoadSessionsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to delete session '{session.Name}': {ex.Message}";
            IsLoading = false;
        }
    }
}
