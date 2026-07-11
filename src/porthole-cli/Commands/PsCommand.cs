using System.CommandLine;
using System.Text.Json;
using porthole_cli.State;

namespace porthole_cli.Commands;

/// <summary>
/// Emulates docker ps — required by Dev Containers for container discovery.
/// </summary>
internal sealed class PsCommand : Command
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        PropertyNamingPolicy = null,
    };

    public PsCommand() : base("ps", "List containers.")
    {
        var allOption = new Option<bool>("--all", ["-a"])
        {
            Description = "Show all containers (default shows just running).",
        };
        var filterOption = new Option<string[]>("--filter", ["-f"])
        {
            Description = "Filter output based on conditions provided.",
            Arity = ArgumentArity.ZeroOrMore,
        };
        var formatOption = new Option<string?>("--format")
        {
            Description = "Format output.",
        };
        var quietOption = new Option<bool>("--quiet", ["-q"])
        {
            Description = "Only display container IDs.",
        };

        Add(allOption);
        Add(filterOption);
        Add(formatOption);
        Add(quietOption);

        this.SetAction((ParseResult parseResult) =>
        {
            bool all = parseResult.GetValue(allOption);
            string[] filters = parseResult.GetValue(filterOption) ?? [];
            bool quiet = parseResult.GetValue(quietOption);

            return Handle(all, filters, quiet);
        });
    }

    private static int Handle(bool all, string[] filters, bool quiet)
    {
        try
        {
            var entries = ContainerStateStore.GetAll();

            // Apply label/name filters that Dev Containers commonly uses.
            // e.g. --filter label=devcontainer.local_folder=...
            foreach (string filter in filters)
            {
                int eq = filter.IndexOf('=', StringComparison.Ordinal);
                if (eq < 0) continue;

                string key = filter[..eq];
                string value = filter[(eq + 1)..];

                if (string.Equals(key, "name", StringComparison.OrdinalIgnoreCase))
                {
                    entries = entries
                        .Where(e => !string.IsNullOrEmpty(e.Name)
                            && e.Name.Contains(value, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                }
                // label= filters: attempt to match against stored inspect labels
                else if (string.Equals(key, "label", StringComparison.OrdinalIgnoreCase))
                {
                    entries = entries.Where(e => InspectHasLabel(e.InspectJson, value)).ToList();
                }
            }

            if (quiet)
            {
                foreach (var e in entries)
                    Console.Out.WriteLine(e.Id);
                return 0;
            }

            // Return JSON array shape Dev Containers expects.
            var rows = entries.Select(e => BuildRow(e)).ToArray();
            Console.Out.WriteLine(JsonSerializer.Serialize(rows, JsonOptions));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static object BuildRow(StoredContainerRecord e)
    {
        // Parse running state from stored inspect JSON.
        bool running = false;
        string status = "exited";
        try
        {
            using var doc = JsonDocument.Parse(e.InspectJson);
            if (doc.RootElement.TryGetProperty("State", out var stateElem)
                && stateElem.ValueKind == JsonValueKind.Object
                && stateElem.TryGetProperty("Running", out var runningElem)
                && runningElem.ValueKind == JsonValueKind.True)
            {
                running = true;
                status = "running";
            }
        }
        catch { }

        return new
        {
            Id = e.Id,
            Names = new[] { "/" + (e.Name ?? e.Id[..12]) },
            State = running ? "running" : "exited",
            Status = status,
            Labels = (object)new { },
        };
    }

    private static bool InspectHasLabel(string inspectJson, string labelFilter)
    {
        // labelFilter may be "key=value" or just "key"
        try
        {
            using var doc = JsonDocument.Parse(inspectJson);
            if (!doc.RootElement.TryGetProperty("Labels", out var labels)
                || labels.ValueKind != JsonValueKind.Object)
                return false;

            int sep = labelFilter.IndexOf('=', StringComparison.Ordinal);
            if (sep < 0)
            {
                return labels.TryGetProperty(labelFilter, out _);
            }

            string lKey = labelFilter[..sep];
            string lVal = labelFilter[(sep + 1)..];
            return labels.TryGetProperty(lKey, out var lElem)
                && string.Equals(lElem.GetString(), lVal, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }
}
