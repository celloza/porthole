using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Porthole.Core.Models;
using Porthole.Tray.Services;

namespace Porthole.Tray;

internal sealed class TrayFlyoutForm : Form
{
    // DWM attribute for rounded corners (Windows 11+).
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;

    [DllImport("dwmapi.dll", PreserveSig = false)]
    private static extern void DwmSetWindowAttribute(nint hwnd, int attr, ref int attrValue, int attrSize);

    // Colors — matches a Windows 11 dark flyout aesthetic.
    private static readonly Color BackgroundColor = Color.FromArgb(30, 30, 30);
    private static readonly Color CardColor = Color.FromArgb(42, 42, 42);
    private static readonly Color AccentColor = Color.FromArgb(0, 120, 212);
    private static readonly Color RunningColor = Color.FromArgb(22, 163, 74);
    private static readonly Color StoppedColor = Color.FromArgb(156, 163, 175);
    private static readonly Color DangerColor = Color.FromArgb(220, 38, 38);
    private static readonly Color TextPrimary = Color.FromArgb(242, 242, 242);
    private static readonly Color TextSecondary = Color.FromArgb(160, 160, 160);
    private static readonly Color SeparatorColor = Color.FromArgb(55, 55, 55);

    private const int FlyoutWidth = 330;
    private const int HeaderHeight = 48;
    private const int FooterHeight = 52;
    private const int SessionCardHeight = 106;
    private const int MaxVisibleSessions = 5;
    private const int CardPadding = 12;

    private readonly WslcBackendService _backendService;
    private readonly Action _openDashboard;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private Panel _sessionsContainer = null!;
    private Label _headerLabel = null!;
    private Label _sessionCountLabel = null!;

    public TrayFlyoutForm(WslcBackendService backendService, Action openDashboard)
    {
        _backendService = backendService;
        _openDashboard = openDashboard;

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        _refreshTimer.Tick += (_, _) => Refresh_();

        InitializeComponents();
    }

    private void InitializeComponents()
    {
        SuspendLayout();

        Text = "Porthole";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = BackgroundColor;
        ForeColor = TextPrimary;
        Padding = new Padding(0);
        AutoScaleMode = AutoScaleMode.Dpi;

        // ── Header ──────────────────────────────────────────────────────────
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = HeaderHeight,
            BackColor = BackgroundColor,
            Padding = new Padding(CardPadding, 0, CardPadding, 0),
        };

        _headerLabel = new Label
        {
            Text = "Porthole",
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            ForeColor = TextPrimary,
            AutoSize = false,
            Width = 180,
            Height = HeaderHeight,
            Top = 0,
            Left = CardPadding,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        _sessionCountLabel = new Label
        {
            Text = string.Empty,
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = TextSecondary,
            AutoSize = false,
            Width = 160,
            Height = HeaderHeight,
            Top = 0,
            Left = FlyoutWidth - 170,
            TextAlign = ContentAlignment.MiddleRight,
        };

        var closeButton = new Button
        {
            Text = "✕",
            Font = new Font("Segoe UI", 9f),
            ForeColor = TextSecondary,
            BackColor = BackgroundColor,
            FlatStyle = FlatStyle.Flat,
            Width = 36,
            Height = 36,
            Top = (HeaderHeight - 36) / 2,
            Left = FlyoutWidth - 44,
            Cursor = Cursors.Hand,
            TabStop = false,
        };
        closeButton.FlatAppearance.BorderSize = 0;
        closeButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(60, 60, 60);
        closeButton.Click += (_, _) => Hide();

        header.Controls.AddRange([_headerLabel, _sessionCountLabel, closeButton]);

        // ── Separator ───────────────────────────────────────────────────────
        var topSeparator = new Panel
        {
            Dock = DockStyle.Top,
            Height = 1,
            BackColor = SeparatorColor,
        };

        // ── Sessions scroll area ─────────────────────────────────────────────
        _sessionsContainer = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = BackgroundColor,
            Padding = new Padding(CardPadding, CardPadding, CardPadding, 0),
        };

        // ── Bottom separator ─────────────────────────────────────────────────
        var bottomSeparator = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 1,
            BackColor = SeparatorColor,
        };

        // ── Footer ───────────────────────────────────────────────────────────
        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = FooterHeight,
            BackColor = BackgroundColor,
            Padding = new Padding(CardPadding, 8, CardPadding, 8),
        };

        var openButton = CreateStyledButton("Open Dashboard", AccentColor, true);
        openButton.Dock = DockStyle.Fill;
        openButton.Click += (_, _) =>
        {
            Hide();
            _openDashboard();
        };
        footer.Controls.Add(openButton);

        Controls.Add(_sessionsContainer);
        Controls.Add(bottomSeparator);
        Controls.Add(footer);
        Controls.Add(topSeparator);
        Controls.Add(header);

        ResumeLayout(true);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        // Apply Windows 11 rounded corners via DWM.
        try
        {
            int cornerPreference = DwmwcpRound;
            DwmSetWindowAttribute(Handle, DwmwaWindowCornerPreference, ref cornerPreference, sizeof(int));
        }
        catch
        {
            // Older Windows versions don't support this attribute — ignore gracefully.
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Refresh_();
        _refreshTimer.Start();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible)
        {
            _refreshTimer.Stop();
        }
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        // Dismiss flyout when another window is activated (click-outside-to-close).
        if (Visible)
        {
            Hide();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Positions the flyout above the taskbar, near the notification area, then shows it.
    /// </summary>
    public void ShowNearTray()
    {
        PositionNearTray();
        if (!Visible)
        {
            Show();
        }
        else
        {
            Activate();
        }
    }

    private void PositionNearTray()
    {
        Rectangle workArea = Screen.PrimaryScreen?.WorkingArea ?? SystemInformation.WorkingArea;
        int formHeight = CalculateRequiredHeight();
        Size = new Size(FlyoutWidth, formHeight);
        Left = workArea.Right - FlyoutWidth - 12;
        Top = workArea.Bottom - formHeight - 12;
    }

    private int CalculateRequiredHeight()
    {
        IReadOnlyList<SessionSnapshot> snapshots = _backendService.GetTraySnapshot();
        int sessionCount = Math.Min(snapshots.Count, MaxVisibleSessions);
        int sessionsHeight = sessionCount == 0
            ? 80  // Empty state placeholder
            : sessionCount * (SessionCardHeight + CardPadding) + CardPadding;

        return HeaderHeight + 1 + sessionsHeight + 1 + FooterHeight;
    }

    private void Refresh_()
    {
        if (!Visible || IsDisposed)
        {
            return;
        }

        try
        {
            IReadOnlyList<SessionSnapshot> snapshots = _backendService.GetTraySnapshot();
            if (InvokeRequired)
            {
                Invoke(() => UpdateSessionCards(snapshots));
            }
            else
            {
                UpdateSessionCards(snapshots);
            }
        }
        catch
        {
            // Don't crash the flyout on a failed refresh.
        }
    }

    private void UpdateSessionCards(IReadOnlyList<SessionSnapshot> snapshots)
    {
        if (IsDisposed)
        {
            return;
        }

        _sessionsContainer.SuspendLayout();
        _sessionsContainer.Controls.Clear();

        _sessionCountLabel.Text = snapshots.Count == 0
            ? string.Empty
            : $"{snapshots.Count} session{(snapshots.Count == 1 ? string.Empty : "s")}";

        if (snapshots.Count == 0)
        {
            var emptyLabel = new Label
            {
                Text = "No sessions running.\nOpen the dashboard to create one.",
                Font = new Font("Segoe UI", 9f),
                ForeColor = TextSecondary,
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = new Padding(8),
            };
            _sessionsContainer.Controls.Add(emptyLabel);
        }
        else
        {
            int yOffset = CardPadding;
            foreach (SessionSnapshot snapshot in snapshots)
            {
                Panel card = CreateSessionCard(snapshot);
                card.Top = yOffset;
                _sessionsContainer.Controls.Add(card);
                yOffset += SessionCardHeight + CardPadding;
            }
        }

        _sessionsContainer.ResumeLayout(true);
        PositionNearTray();
    }

    private Panel CreateSessionCard(SessionSnapshot snapshot)
    {
        bool isRunning = string.Equals(snapshot.Status, "Running", StringComparison.OrdinalIgnoreCase);
        Color statusColor = isRunning ? RunningColor : StoppedColor;

        var card = new Panel
        {
            Left = CardPadding,
            Width = FlyoutWidth - CardPadding * 2,
            Height = SessionCardHeight,
            BackColor = CardColor,
            Padding = new Padding(10, 8, 10, 8),
        };

        // Status dot
        var statusDot = new Panel
        {
            Size = new Size(8, 8),
            Top = 14,
            Left = 10,
            BackColor = statusColor,
        };
        MakeCircular(statusDot);

        // Active badge
        int nameLeft = 24;
        Label? activeBadge = null;
        if (snapshot.IsActive)
        {
            activeBadge = new Label
            {
                Text = "Active",
                Font = new Font("Segoe UI", 7f, FontStyle.Bold),
                ForeColor = AccentColor,
                AutoSize = true,
                Top = 11,
                Left = 24,
            };
            activeBadge.Width = activeBadge.PreferredWidth;
            nameLeft = 24 + activeBadge.Width + 6;
        }

        // Session name
        var nameLabel = new Label
        {
            Text = snapshot.Name,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
            ForeColor = TextPrimary,
            AutoSize = false,
            Width = card.Width - nameLeft - 10,
            Height = 20,
            Top = 10,
            Left = nameLeft,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        // Status label
        var statusLabel = new Label
        {
            Text = snapshot.Status,
            Font = new Font("Segoe UI", 8f),
            ForeColor = statusColor,
            AutoSize = false,
            Width = 80,
            Height = 18,
            Top = 30,
            Left = 10,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        // Separator line inside card
        var internalSep = new Panel
        {
            BackColor = SeparatorColor,
            Height = 1,
            Top = 54,
            Left = 10,
            Width = card.Width - 20,
        };

        // Action buttons row
        int buttonY = 62;
        int buttonH = 28;

        if (snapshot.IsActive)
        {
            // Active session: Pause/Resume + Terminate (no Set Active needed).
            if (isRunning)
            {
                var pauseBtn = CreateActionButton("⏸ Pause", StoppedColor, buttonY, 10, 88, buttonH);
                pauseBtn.Click += (_, _) => RunAction(() => _backendService.PauseSession(snapshot.Name));

                var terminateBtn = CreateActionButton("⏹ Terminate", DangerColor, buttonY, 104, 104, buttonH);
                terminateBtn.Click += (_, _) =>
                {
                    if (ConfirmTerminate(snapshot.Name))
                    {
                        RunAction(() => _backendService.TerminateNamedSession(snapshot.Name));
                    }
                };

                card.Controls.AddRange([pauseBtn, terminateBtn]);
            }
            else
            {
                var resumeBtn = CreateActionButton("▶ Resume", RunningColor, buttonY, 10, 88, buttonH);
                resumeBtn.Click += (_, _) => RunAction(() => _backendService.ResumeSession(snapshot.Name));

                var terminateBtn = CreateActionButton("⏹ Terminate", DangerColor, buttonY, 104, 104, buttonH);
                terminateBtn.Click += (_, _) =>
                {
                    if (ConfirmTerminate(snapshot.Name))
                    {
                        RunAction(() => _backendService.TerminateNamedSession(snapshot.Name));
                    }
                };

                card.Controls.AddRange([resumeBtn, terminateBtn]);
            }
        }
        else
        {
            // Inactive session: Set Active + Pause/Resume + Terminate (three-button row).
            var setActiveBtn = CreateActionButton("⊙ Set Active", AccentColor, buttonY, 10, 82, buttonH);
            setActiveBtn.Click += (_, _) => RunAction(() => _backendService.SetActiveSession(snapshot.Name));

            if (isRunning)
            {
                var pauseBtn = CreateActionButton("⏸ Pause", StoppedColor, buttonY, 96, 82, buttonH);
                pauseBtn.Click += (_, _) => RunAction(() => _backendService.PauseSession(snapshot.Name));

                var terminateBtn = CreateActionButton("⏹ Terminate", DangerColor, buttonY, 182, 104, buttonH);
                terminateBtn.Click += (_, _) =>
                {
                    if (ConfirmTerminate(snapshot.Name))
                    {
                        RunAction(() => _backendService.TerminateNamedSession(snapshot.Name));
                    }
                };

                card.Controls.AddRange([setActiveBtn, pauseBtn, terminateBtn]);
            }
            else
            {
                var resumeBtn = CreateActionButton("▶ Resume", RunningColor, buttonY, 96, 82, buttonH);
                resumeBtn.Click += (_, _) => RunAction(() => _backendService.ResumeSession(snapshot.Name));

                var terminateBtn = CreateActionButton("⏹ Terminate", DangerColor, buttonY, 182, 104, buttonH);
                terminateBtn.Click += (_, _) =>
                {
                    if (ConfirmTerminate(snapshot.Name))
                    {
                        RunAction(() => _backendService.TerminateNamedSession(snapshot.Name));
                    }
                };

                card.Controls.AddRange([setActiveBtn, resumeBtn, terminateBtn]);
            }
        }

        card.Controls.AddRange([statusDot, nameLabel, statusLabel, internalSep]);
        if (activeBadge is not null)
        {
            card.Controls.Add(activeBadge);
        }

        // Rounded corners for the card via region.
        card.Paint += (_, e) =>
        {
            using var pen = new Pen(SeparatorColor, 1);
            e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
        };

        return card;
    }

    private static void MakeCircular(Panel panel)
    {
        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddEllipse(0, 0, panel.Width, panel.Height);
        panel.Region = new Region(path);
    }

    private static Button CreateActionButton(string text, Color foreColor, int top, int left, int width, int height)
    {
        var btn = new Button
        {
            Text = text,
            Font = new Font("Segoe UI", 8f),
            ForeColor = foreColor,
            BackColor = Color.FromArgb(55, 55, 55),
            FlatStyle = FlatStyle.Flat,
            Top = top,
            Left = left,
            Width = width,
            Height = height,
            Cursor = Cursors.Hand,
            TabStop = false,
            TextAlign = ContentAlignment.MiddleCenter,
        };
        btn.FlatAppearance.BorderSize = 0;
        btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(70, 70, 70);
        return btn;
    }

    private static Button CreateStyledButton(string text, Color backColor, bool primary)
    {
        var btn = new Button
        {
            Text = text,
            Font = new Font("Segoe UI", 9f, primary ? FontStyle.Bold : FontStyle.Regular),
            ForeColor = primary ? Color.White : TextPrimary,
            BackColor = backColor,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            TabStop = false,
        };
        btn.FlatAppearance.BorderSize = 0;
        btn.FlatAppearance.MouseOverBackColor = ControlPaint.Light(backColor, 0.2f);
        return btn;
    }

    private void RunAction(Action action)
    {
        // Run on a thread-pool thread to avoid blocking the UI for sync WSL SDK calls.
        _ = Task.Run(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                if (!IsDisposed)
                {
                    Invoke(() => MessageBox.Show(
                        ex.Message,
                        "Porthole — Action failed",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning));
                }

                return;
            }

            if (!IsDisposed)
            {
                Invoke(Refresh_);
            }
        });
    }

    private static bool ConfirmTerminate(string sessionName)
    {
        return MessageBox.Show(
            $"Terminate session '{sessionName}'? This will stop all its containers and remove the session from Porthole.\n\nUnderlying VHD storage is preserved on disk and can be re-attached manually.",
            "Porthole — Confirm terminate",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2) == DialogResult.Yes;
    }
}
