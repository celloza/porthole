using System.CommandLine;
using Microsoft.WSL.Containers;
using porthole_cli.State;

namespace porthole_cli.Commands;

/// <summary>
/// Emulates docker exec — required by Dev Containers to attach and run commands inside a container.
/// </summary>
internal sealed class ExecCommand : Command
{
    private const string DevContainerSessionName = "porthole-devcontainers";

    public ExecCommand() : base("exec", "Run a command in a running container.")
    {
        var containerArgument = new Argument<string>("container")
        {
            Description = "Container ID or name.",
        };
        var commandArgument = new Argument<string[]>("command")
        {
            Description = "Command and arguments to execute.",
            Arity = ArgumentArity.OneOrMore,
        };

        // Common docker exec options Dev Containers passes.
        var interactiveOption = new Option<bool>("--interactive", ["-i"])
        {
            Description = "Keep STDIN open.",
        };
        var ttyOption = new Option<bool>("--tty", ["-t"])
        {
            Description = "Allocate a pseudo-TTY.",
        };
        var detachOption = new Option<bool>("--detach", ["-d"])
        {
            Description = "Run command in the background.",
        };
        var workdirOption = new Option<string?>("--workdir", ["-w"])
        {
            Description = "Working directory inside the container.",
        };
        var envOption = new Option<string[]>("--env", ["-e"])
        {
            Description = "Set environment variables.",
            Arity = ArgumentArity.ZeroOrMore,
        };
        var userOption = new Option<string?>("--user", ["-u"])
        {
            Description = "Username or UID.",
        };

        Add(containerArgument);
        Add(commandArgument);
        Add(interactiveOption);
        Add(ttyOption);
        Add(detachOption);
        Add(workdirOption);
        Add(envOption);
        Add(userOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
        {
            string container = parseResult.GetRequiredValue(containerArgument);
            string[] command = parseResult.GetRequiredValue(commandArgument);
            bool detach = parseResult.GetValue(detachOption);
            string? workdir = parseResult.GetValue(workdirOption);
            string[] envVars = parseResult.GetValue(envOption) ?? [];

            return await HandleAsync(container, command, detach, workdir, envVars, cancellationToken);
        });
    }

    private static async Task<int> HandleAsync(
        string container,
        string[] command,
        bool detach,
        string? workdir,
        string[] envVars,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!ContainerStateStore.TryGet(container.Trim(), out StoredContainerRecord? stored) || stored is null)
            {
                Console.Error.WriteLine($"Container '{container}' was not found in porthole-cli state.");
                return 1;
            }

            string storagePath = GetSessionStoragePath();
            var sessionSettings = new SessionSettings(DevContainerSessionName, storagePath);
            using var session = new Session(sessionSettings);
            session.Start();

            // Locate the container handle by recreating a settings object pointing to the stored id.
            // The SDK requires CreateContainer to get a handle, but we need to find an existing one.
            // We use a separate ProcessSettings approach: create process on the container's init process
            // is not available for lookup, so we use CreateContainer with the same name to attach.
            // The SDK does not expose an open-by-id API yet; exec is run via Container.CreateProcess
            // which requires a Container handle. We work around this by creating a fresh container
            // handle targeting the same image and name — the SDK will attach to the existing one
            // if a container with that name already exists.
            var containerSettings = new ContainerSettings(GetImageFromInspect(stored.InspectJson))
            {
                Name = stored.Name ?? stored.Id[..12],
            };

            using var containerHandle = session.CreateContainer(containerSettings);

            var procSettings = new ProcessSettings
            {
                CommandLine = command,
                OutputMode = detach ? ProcessOutputMode.Discard : ProcessOutputMode.Stream,
            };

            if (!string.IsNullOrWhiteSpace(workdir))
            {
                procSettings.WorkingDirectory = workdir;
            }

            foreach (string env in envVars)
            {
                int eq = env.IndexOf('=', StringComparison.Ordinal);
                if (eq > 0)
                {
                    procSettings.EnvironmentVariables[env[..eq]] = env[(eq + 1)..];
                }
            }

            using var proc = containerHandle.CreateProcess(procSettings);

            if (!detach)
            {
                // Wire stdout/stderr from the SDK process to our streams.
                var stdoutDone = new TaskCompletionSource();
                var stderrDone = new TaskCompletionSource();
                int exitCode = 0;

                proc.OutputReceived += data => Console.Out.Write(System.Text.Encoding.UTF8.GetString(data));
                proc.ErrorReceived += data => Console.Error.Write(System.Text.Encoding.UTF8.GetString(data));
                proc.Exited += code =>
                {
                    exitCode = code;
                    stdoutDone.TrySetResult();
                    stderrDone.TrySetResult();
                };

                proc.Start();
                await Task.WhenAny(
                    Task.WhenAll(stdoutDone.Task, stderrDone.Task),
                    Task.Delay(TimeSpan.FromMinutes(10), cancellationToken));

                return exitCode;
            }
            else
            {
                proc.Start();
                await Task.CompletedTask;
                return 0;
            }
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("exec operation canceled.");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static string GetImageFromInspect(string inspectJson)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(inspectJson);
            if (doc.RootElement.TryGetProperty("Image", out var imgElem))
                return imgElem.GetString() ?? "alpine:latest";
        }
        catch { }
        return "alpine:latest";
    }

    private static string GetSessionStoragePath()
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Porthole",
            "Sessions",
            DevContainerSessionName);
        Directory.CreateDirectory(path);
        return path;
    }
}
