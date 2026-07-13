using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Porthole.Core.Models;
using Porthole.Core.Services;

namespace Porthole.Core.ViewModels;

public partial class ShellViewModel : ObservableObject
{
    private readonly ISessionService _sessionService;
    private bool _suppressAutoSwitch;

    public ObservableCollection<SessionSummary> Sessions { get; } = [];

    [ObservableProperty]
    private string windowTitle = "Porthole";

    [ObservableProperty]
    private string currentSectionTitle = "System Dashboard";

    [ObservableProperty]
    private string activeSessionName = string.Empty;

    [ObservableProperty]
    private string sessionStatus = "Loading session context...";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshSessionsCommand))]
    [NotifyCanExecuteChangedFor(nameof(SwitchSelectedSessionCommand))]
    private bool isSessionLoading;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SwitchSelectedSessionCommand))]
    private SessionSummary? selectedSession;

    public string ActiveSessionDisplay => string.IsNullOrWhiteSpace(ActiveSessionName)
        ? "No active session"
        : $"Session: {ActiveSessionName}";

    public bool HasSessions => Sessions.Count > 0;

    public ShellViewModel(ISessionService sessionService)
    {
        _sessionService = sessionService;
        Sessions.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasSessions));
        _sessionService.SessionsChanged += OnSessionsChanged;
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _sessionService.StartWatchingForChanges(cancellationToken);
        return LoadSessionsCoreAsync(cancellationToken);
    }

    public void Cleanup()
    {
        _sessionService.StopWatchingForChanges();
        _sessionService.SessionsChanged -= OnSessionsChanged;
    }

    private void OnSessionsChanged(object? sender, EventArgs e)
    {
        _ = LoadSessionsCoreAsync(CancellationToken.None, suppressLoadingState: true);
    }

    public void SetSection(string sectionTitle)
    {
        CurrentSectionTitle = sectionTitle;
    }

    [RelayCommand(CanExecute = nameof(CanRefreshSessions))]
    private Task RefreshSessionsAsync(CancellationToken cancellationToken = default) => LoadSessionsCoreAsync(cancellationToken);

    private bool CanRefreshSessions() => !IsSessionLoading;

    [RelayCommand(CanExecute = nameof(CanSwitchSelectedSession))]
    private async Task SwitchSelectedSessionAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedSession is null || string.Equals(SelectedSession.Name, ActiveSessionName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        IsSessionLoading = true;
        SessionStatus = $"Switching to session '{SelectedSession.Name}'...";

        try
        {
            await _sessionService.SetActiveSessionAsync(SelectedSession.Name, cancellationToken);
            await LoadSessionsCoreAsync(cancellationToken, suppressLoadingState: true);
        }
        catch (Exception ex)
        {
            SessionStatus = $"Failed to switch session: {ex.Message}";
        }
        finally
        {
            IsSessionLoading = false;
        }
    }

    private bool CanSwitchSelectedSession() =>
        !IsSessionLoading
        && SelectedSession is not null
        && !string.Equals(SelectedSession.Name, ActiveSessionName, StringComparison.OrdinalIgnoreCase);

    private async Task LoadSessionsCoreAsync(CancellationToken cancellationToken, bool suppressLoadingState = false)
    {
        if (!suppressLoadingState)
        {
            IsSessionLoading = true;
            SessionStatus = "Loading sessions...";
        }

        try
        {
            IReadOnlyList<SessionSummary> sessions = await _sessionService.ListSessionsAsync(cancellationToken);
            string activeName = await _sessionService.GetActiveSessionNameAsync(cancellationToken);

            Sessions.Clear();
            foreach (SessionSummary session in sessions)
            {
                Sessions.Add(session);
            }

            ActiveSessionName = activeName;
            OnPropertyChanged(nameof(ActiveSessionDisplay));

            _suppressAutoSwitch = true;
            SelectedSession = Sessions.FirstOrDefault(session => session.IsActive)
                ?? Sessions.FirstOrDefault(session => string.Equals(session.Name, activeName, StringComparison.OrdinalIgnoreCase))
                ?? Sessions.FirstOrDefault();
            _suppressAutoSwitch = false;

            SessionStatus = Sessions.Count == 0
                ? "No sessions available."
                : $"{Sessions.Count} session{(Sessions.Count == 1 ? string.Empty : "s")} loaded.";
        }
        catch (Exception ex)
        {
            SessionStatus = $"Failed to load sessions: {ex.Message}";
        }
        finally
        {
            IsSessionLoading = false;
        }
    }

    partial void OnSelectedSessionChanged(SessionSummary? value)
    {
        if (_suppressAutoSwitch || value is null || IsSessionLoading)
        {
            return;
        }

        if (string.Equals(value.Name, ActiveSessionName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _ = SwitchSelectedSessionAsync();
    }
}