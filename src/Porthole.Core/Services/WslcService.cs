using System.IO;
using System.Text.Json;
using Porthole.Core.Models;
using Porthole.Core.Services.NamedPipe;

namespace Porthole.Core.Services;

public sealed class WslcService : IWslcService
{
    private readonly NamedPipeDashboardSnapshotService _dashboardSnapshotService = new();

    public WslcPrerequisiteReport GetMissingComponents()
    {
        List<string> missingComponents = new();

        if (!CommandExists("wsl.exe"))
        {
            missingComponents.Add("Windows Subsystem for Linux");
        }

        if (!CommandExists("wslc.exe"))
        {
            missingComponents.Add("WSL Containers prerelease components");
        }

        return new WslcPrerequisiteReport(missingComponents);
    }

    public Task<DashboardSnapshot> GetDashboardSnapshotAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return GetDashboardSnapshotCoreAsync(cancellationToken);
    }

    private async Task<DashboardSnapshot> GetDashboardSnapshotCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _dashboardSnapshotService.GetDashboardSnapshotAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException or OperationCanceledException)
        {
            return await GetLocalDashboardSnapshotAsync(cancellationToken);
        }
    }

    private static async Task<DashboardSnapshot> GetLocalDashboardSnapshotAsync(CancellationToken cancellationToken)
    {
        try
        {
            string listJson = await RunWslcCommandAsync("list --all --format json", cancellationToken);
            using JsonDocument listDocument = JsonDocument.Parse(listJson);

            int running = 0;
            int total = 0;

            foreach (JsonElement container in listDocument.RootElement.EnumerateArray())
            {
                total++;
                if (container.TryGetProperty("State", out JsonElement stateElement) && stateElement.GetInt32() == 2)
                {
                    running++;
                }
            }

            int stopped = Math.Max(0, total - running);

            string cpu = "Idle";
            string memory = "Idle";
            double totalCpu = 0;

            if (running > 0)
            {
                string statsJson = await RunWslcCommandAsync("stats --format json", cancellationToken);
                using JsonDocument statsDocument = JsonDocument.Parse(statsJson);

                foreach (JsonElement stat in statsDocument.RootElement.EnumerateArray())
                {
                    if (stat.TryGetProperty("CPUPerc", out JsonElement cpuElement))
                    {
                        string cpuText = cpuElement.GetString() ?? string.Empty;
                        cpuText = cpuText.Trim().TrimEnd('%');
                        if (double.TryParse(cpuText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value))
                        {
                            totalCpu += value;
                        }
                    }

                    if (memory == "Idle" && stat.TryGetProperty("MemUsage", out JsonElement memoryElement))
                    {
                        memory = memoryElement.GetString() ?? "Telemetry unavailable";
                    }
                }

                cpu = $"{totalCpu:0.##}%";
                if (memory == "Idle")
                {
                    memory = "Telemetry unavailable";
                }
            }

            return new DashboardSnapshot(
                cpu,
                memory,
                $"{running} running",
                "Running in local telemetry mode because tray snapshot API is unavailable.")
            {
                CpuPercent = totalCpu,
            };
        }
        catch
        {
            return new DashboardSnapshot(
                "Unavailable",
                "Unavailable",
                "0 running",
                "Tray host unavailable and local telemetry fallback failed.");
        }
    }

    private static async Task<string> RunWslcCommandAsync(string arguments, CancellationToken cancellationToken)
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("wslc", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException("Failed to start wslc.");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        Task<string> stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

        await process.WaitForExitAsync(timeoutCts.Token);
        await Task.WhenAll(stdoutTask, stderrTask);

        string stdout = await stdoutTask;
        string stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr)
                ? $"wslc {arguments} failed with exit code {process.ExitCode}."
                : stderr.Trim());
        }

        return stdout;
    }

    private static bool CommandExists(string fileName)
    {
        foreach (string candidate in GetCandidatePaths(fileName))
        {
            if (File.Exists(candidate))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> GetCandidatePaths(string fileName)
    {
        yield return Path.Combine(Environment.SystemDirectory, fileName);
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WSL", fileName);

        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            yield break;
        }

        foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string candidate = Path.Combine(directory, fileName);
            yield return candidate;
        }
    }
}