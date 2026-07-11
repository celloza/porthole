using System.CommandLine;
using Microsoft.WSL.Containers;
using porthole_cli.State;

namespace porthole_cli.Commands;

internal sealed class RunCommand : Command
{
    private const string DevContainerSessionName = "porthole-devcontainers";

    public RunCommand() : base("run", "Run a container with DevContainer-compatible Docker flags.")
    {
        var detachOption = new Option<bool>("--detach", ["-d"])
        {
            Description = "Run the container in detached mode.",
        };
        var volumeOption = new Option<string[]>("--volume", ["-v"])
        {
            Description = "Bind mount or volume mapping.",
            Arity = ArgumentArity.ZeroOrMore,
            AllowMultipleArgumentsPerToken = true,
        };
        var publishOption = new Option<string[]>("--publish", ["-p"])
        {
            Description = "Publish container ports.",
            Arity = ArgumentArity.ZeroOrMore,
            AllowMultipleArgumentsPerToken = true,
        };
        var nameOption = new Option<string?>("--name")
        {
            Description = "Container name.",
        };

        var imageArgument = new Argument<string>("image")
        {
            Description = "Image reference to run.",
        };
        var commandArgument = new Argument<string[]>("command")
        {
            Description = "Optional command and args.",
            Arity = ArgumentArity.ZeroOrMore,
        };

        Add(detachOption);
        Add(volumeOption);
        Add(publishOption);
        Add(nameOption);
        Add(imageArgument);
        Add(commandArgument);

        this.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
        {
            bool detach = parseResult.GetValue(detachOption);
            string[] volumes = parseResult.GetValue(volumeOption) ?? [];
            string[] publish = parseResult.GetValue(publishOption) ?? [];
            string? name = parseResult.GetValue(nameOption);
            string image = parseResult.GetRequiredValue(imageArgument);
            string[] command = parseResult.GetValue(commandArgument) ?? [];

            return await HandleAsync(detach, volumes, publish, name, image, command, cancellationToken);
        });
    }

    private static async Task<int> HandleAsync(bool detach, string[] volumes, string[] publish, string? name, string image, string[] command, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(image))
            {
                Console.Error.WriteLine("Image argument is required.");
                return 1;
            }

            // Ensure a session exists so volume mappings are projected via virtiofs within
            // the active WSL Containers session.
            var sessionSettings = new SessionSettings(DevContainerSessionName, GetSessionStoragePath())
            {
                CpuCount = 4,
                MemorySizeInMB = 4096,
                VhdRequirements = new VhdOptions(string.Empty, 8UL * 1024 * 1024 * 1024, VhdType.Dynamic),
            };

            using var session = new Session(sessionSettings);
            session.Start();

            var containerSettings = new ContainerSettings(image.Trim());

            if (!string.IsNullOrWhiteSpace(name))
            {
                containerSettings.Name = name.Trim();
            }

            var mounts = new List<ContainerVolume>();
            foreach (string volume in volumes)
            {
                if (string.IsNullOrWhiteSpace(volume))
                {
                    continue;
                }

                var mount = RunArgumentParser.ParseVolume(volume);
                mounts.Add(new ContainerVolume(mount.HostPath, mount.ContainerPath, mount.ReadOnly));
            }

            if (mounts.Count > 0)
            {
                containerSettings.Volumes = mounts;
            }

            var portMappings = new List<ContainerPortMapping>();
            foreach (string port in publish)
            {
                if (string.IsNullOrWhiteSpace(port))
                {
                    continue;
                }

                portMappings.Add(RunArgumentParser.ParsePortMapping(port));
            }

            if (portMappings.Count > 0)
            {
                containerSettings.PortMappings = portMappings;
            }

            if (command.Length > 0)
            {
                containerSettings.InitProcess = new ProcessSettings
                {
                    CommandLine = command,
                };
            }

            using var container = session.CreateContainer(containerSettings);
            container.Start();

            string inspectJson = container.Inspect();
            ContainerStateStore.Upsert(new StoredContainerRecord(
                container.Id,
                containerSettings.Name,
                inspectJson,
                DateTimeOffset.UtcNow));

            if (detach)
            {
                // Docker-compatible detached mode prints only container ID to stdout.
                Console.Out.WriteLine(container.Id);
            }

            await Task.CompletedTask;
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("run operation canceled.");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
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
