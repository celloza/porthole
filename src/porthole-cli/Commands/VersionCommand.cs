using System.CommandLine;
using System.Text.Json;

namespace porthole_cli.Commands;

internal sealed class VersionCommand : Command
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
    };

    public VersionCommand() : base("version", "Show Docker-compatible version information.")
    {
        var formatOption = new Option<string?>("--format", ["-f"])
        {
            Description = "Format the output.",
        };
        Add(formatOption);

        this.SetAction((ParseResult parseResult) =>
        {
            // Dev Containers calls `docker version -f json` and parses the Server.ApiVersion field.
            // We emit a minimal shape that passes this check without failures.
            var payload = new
            {
                Client = new
                {
                    Version = "24.0.0",
                    ApiVersion = "1.43",
                    DefaultAPIVersion = "1.43",
                    Os = "windows",
                    Arch = "amd64",
                    BuildTime = "2024-01-01T00:00:00.000000000+00:00",
                },
                Server = new
                {
                    Engine = new
                    {
                        Version = "24.0.0",
                        ApiVersion = "1.43",
                        MinAPIVersion = "1.12",
                        Os = "linux",
                        Arch = "amd64",
                    },
                },
            };

            Console.Out.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
            return 0;
        });
    }
}
