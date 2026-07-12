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
		private readonly DockerApiServer _dockerApiServer;
		private readonly NamedPipeImageCatalogServer _imageServer;
		private readonly ContextMenuStrip _menu;
		private readonly NotifyIcon _notifyIcon;

		public TrayApplicationContext()
		{
			var configurationStore = new TrayConfigurationStore();
			TrayConfiguration configuration = configurationStore.Load();
			var backendService = new WslcBackendService();
			_imageServer = new NamedPipeImageCatalogServer(backendService);
			_dockerApiServer = new DockerApiServer(backendService, configuration.DockerApi);
			_imageServer.Start();
			_dockerApiServer.Start();

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
			_dockerApiServer.Dispose();
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
		string? appPath = ResolveDashboardPath();

		if (appPath is null)
		{
			MessageBox.Show(
				"The Porthole dashboard could not be opened. If this issue persists after reinstalling Porthole, please report it at https://github.com/celloza/porthole/issues.",
				"Porthole — Dashboard unavailable",
				MessageBoxButtons.OK,
				MessageBoxIcon.Warning);
			return;
		}

		if (TryActivateExistingDashboard(appPath))
		{
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
		string? repositoryRoot = FindRepositoryRoot();
		if (repositoryRoot is not null)
		{
			string? repositoryCandidate = ResolveDashboardPathFromRepository(repositoryRoot);
			if (repositoryCandidate is not null)
			{
				return repositoryCandidate;
			}
		}

		foreach (string candidate in GetDashboardPathCandidates())
		{
			if (File.Exists(candidate))
			{
				return candidate;
			}
		}

		return null;
	}

	private static string? ResolveDashboardPathFromRepository(string repositoryRoot)
	{
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

	private static IEnumerable<string> GetDashboardPathCandidates()
	{
		// Prefer the process path over AppContext.BaseDirectory, which can differ
		// (e.g. when the app is launched by an MSI custom action with a relative executable path).
		string? processDir = string.IsNullOrEmpty(Environment.ProcessPath)
			? null
			: Path.GetDirectoryName(Environment.ProcessPath);
		string baseDirectory = processDir ?? AppContext.BaseDirectory;

		yield return Path.Combine(baseDirectory, "Porthole.App.exe");

		// Installed MSI layout places tray and dashboard in sibling folders: ...\Porthole\Tray and ...\Porthole\App
		yield return Path.GetFullPath(Path.Combine(baseDirectory, "..", "App", "Porthole.App.exe"));
		yield return Path.GetFullPath(Path.Combine(baseDirectory, "..", "Porthole.App.exe"));

		// Explicit per-user installation path — reliable when path arithmetic from the process
		// directory is not available or does not resolve to the correct installation layout.
		string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		if (!string.IsNullOrEmpty(localAppData))
		{
			yield return Path.Combine(localAppData, "Porthole", "App", "Porthole.App.exe");
		}
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

	private static bool TryActivateExistingDashboard(string expectedPath)
	{
		foreach (Process process in Process.GetProcessesByName("Porthole.App"))
		{
			if (process.HasExited)
			{
				continue;
			}

			string? runningPath = null;
			try
			{
				runningPath = process.MainModule?.FileName;
			}
			catch
			{
				// Ignore processes where executable path can't be read.
			}

			if (string.IsNullOrWhiteSpace(runningPath) ||
				!string.Equals(Path.GetFullPath(runningPath), Path.GetFullPath(expectedPath), StringComparison.OrdinalIgnoreCase))
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
