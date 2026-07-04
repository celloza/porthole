using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Porthole.Tray.Services;

namespace Porthole.Tray;

internal static class Program
{
	[DllImport("user32.dll")]
	private static extern bool ShowWindow(nint hWnd, int nCmdShow);

	[DllImport("user32.dll")]
	private static extern bool SetForegroundWindow(nint hWnd);

	private const int SwRestore = 9;

	[STAThread]
	private static void Main()
	{
		ApplicationConfiguration.Initialize();
		Application.Run(new TrayApplicationContext());
	}

	private sealed class TrayApplicationContext : ApplicationContext
	{
		private readonly NamedPipeImageCatalogServer _imageServer;
		private readonly ContextMenuStrip _menu;
		private readonly NotifyIcon _notifyIcon;

		public TrayApplicationContext()
		{
			_imageServer = new NamedPipeImageCatalogServer(new WslcBackendService());
			_imageServer.Start();

			_menu = new ContextMenuStrip();
			_menu.Items.Add("Open dashboard", null, (_, _) => LaunchDashboard());
			_menu.Items.Add("Exit", null, (_, _) => ExitThread());

			var trayIcon = LoadTrayIcon();
			_notifyIcon = new NotifyIcon
			{
				Text = "Porthole",
				Icon = trayIcon ?? SystemIcons.Application,
				Visible = true,
				ContextMenuStrip = _menu,
			};

			_notifyIcon.DoubleClick += (_, _) => LaunchDashboard();
			LaunchDashboard();
		}

		protected override void ExitThreadCore()
		{
			_notifyIcon.Visible = false;
			_notifyIcon.Dispose();
			_menu.Dispose();
			_imageServer.Dispose();
			base.ExitThreadCore();
		}
	}

	private static Icon? LoadTrayIcon()
	{
		string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
		return File.Exists(iconPath) ? new Icon(iconPath, 16, 16) : null;
	}

	private static void LaunchDashboard()
	{
		if (TryActivateExistingDashboard())
		{
			return;
		}

		string? appPath = ResolveDashboardPath();

		if (appPath is null)
		{
			MessageBox.Show(
				"Porthole.App.exe could not be found in the tray output or the app project's build output. Build the dashboard once or build the full solution before relying on tray activation.",
				"Porthole",
				MessageBoxButtons.OK,
				MessageBoxIcon.Information);
			return;
		}

		Process.Start(new ProcessStartInfo(appPath)
		{
			UseShellExecute = true,
			WorkingDirectory = Path.GetDirectoryName(appPath),
		});
	}

	private static string? ResolveDashboardPath()
	{
		string localCandidate = Path.Combine(AppContext.BaseDirectory, "Porthole.App.exe");
		if (File.Exists(localCandidate))
		{
			return localCandidate;
		}

		string? repositoryRoot = FindRepositoryRoot();
		if (repositoryRoot is null)
		{
			return null;
		}

		string appBinDirectory = Path.Combine(repositoryRoot, "src", "Porthole.App", "bin");
		if (!Directory.Exists(appBinDirectory))
		{
			return null;
		}

		string configuration = GetCurrentBuildConfiguration();
		string architectureSegment = $"win-{RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}";

		return Directory
			.EnumerateFiles(appBinDirectory, "Porthole.App.exe", SearchOption.AllDirectories)
			.OrderByDescending(path => path.Contains($"{Path.DirectorySeparatorChar}{configuration}{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
			.ThenByDescending(path => path.Contains($"{Path.DirectorySeparatorChar}{architectureSegment}{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
			.ThenByDescending(path => File.GetLastWriteTimeUtc(path))
			.FirstOrDefault();
	}

	private static string? FindRepositoryRoot()
	{
		DirectoryInfo? directory = new(AppContext.BaseDirectory);
		while (directory is not null)
		{
			if (File.Exists(Path.Combine(directory.FullName, "Porthole.slnx")))
			{
				return directory.FullName;
			}

			directory = directory.Parent;
		}

		return null;
	}

	private static string GetCurrentBuildConfiguration()
	{
		string[] segments = AppContext.BaseDirectory
			.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
			.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

		return segments.FirstOrDefault(segment =>
			segment.Equals("Debug", StringComparison.OrdinalIgnoreCase) ||
			segment.Equals("Release", StringComparison.OrdinalIgnoreCase)) ?? "Debug";
	}

	private static bool TryActivateExistingDashboard()
	{
		foreach (Process process in Process.GetProcessesByName("Porthole.App"))
		{
			if (process.HasExited)
			{
				continue;
			}

			nint windowHandle = process.MainWindowHandle;
			if (windowHandle == nint.Zero)
			{
				continue;
			}

			ShowWindow(windowHandle, SwRestore);
			SetForegroundWindow(windowHandle);
			return true;
		}

		return false;
	}
}
