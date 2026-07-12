using System.CommandLine;
using porthole_cli.State;

namespace porthole_cli.Commands;

internal sealed class InspectCommand : Command
{
    public InspectCommand() : base("inspect", "Inspect a container and output Docker-compatible JSON.")
    {
        var containerArgument = new Argument<string>("container")
        {
            Description = "Container ID or name to inspect.",
        };
        Add(containerArgument);

        this.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
        {
            string container = parseResult.GetRequiredValue(containerArgument);
            return await HandleAsync(container, cancellationToken);
        });
    }

    private static async Task<int> HandleAsync(string container, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(container))
            {
                Console.Error.WriteLine("Container argument is required.");
                return 1;
            }

            if (!ContainerStateStore.TryGet(container.Trim(), out StoredContainerRecord? stored) || stored is null)
            {
                Console.Error.WriteLine(
                    $"Container '{container}' was not found. " +
                    "The current Microsoft.WSL.Containers SDK (2.9.3) does not expose a container lookup/list API for live inspect-by-id/name. " +
                    "This shim can inspect containers created through this CLI session registry.");
                return 1;
            }

            string payload = DockerInspectPayloadBuilder.Build(stored.InspectJson);
            Console.Out.WriteLine(payload);
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("inspect operation canceled.");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

}
