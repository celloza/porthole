using System.Text.Json;

namespace porthole_cli.Commands;

internal static class DockerInspectPayloadBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        PropertyNamingPolicy = null,
    };

    public static string Build(string inspectJson)
    {
        using var document = JsonDocument.Parse(inspectJson);

        JsonElement source = document.RootElement;
        string id = source.TryGetProperty("Id", out var idElem) ? idElem.GetString() ?? string.Empty : string.Empty;

        // SDK inspect returns State as an object with a Running boolean (not an integer).
        bool running = false;
        if (source.TryGetProperty("State", out var stateElem))
        {
            if (stateElem.ValueKind == JsonValueKind.Object
                && stateElem.TryGetProperty("Running", out var runningElem)
                && runningElem.ValueKind == JsonValueKind.True)
            {
                running = true;
            }
            else if (stateElem.ValueKind == JsonValueKind.Number && stateElem.GetInt32() == 2)
            {
                // Fallback: older wslc integer state encoding (State == 2 means Running).
                running = true;
            }
        }

        // SDK inspect has Ports as a flat object {"80/tcp": [...]}; re-expose as-is.
        object ports = source.TryGetProperty("Ports", out var portsElem)
            ? JsonSerializer.Deserialize<object>(portsElem.GetRawText(), JsonOptions) ?? new { }
            : new { };

        object mounts = source.TryGetProperty("Mounts", out var mountsElem)
            ? JsonSerializer.Deserialize<object>(mountsElem.GetRawText(), JsonOptions) ?? Array.Empty<object>()
            : Array.Empty<object>();

        var payload = new[]
        {
            new
            {
                Id = id,
                State = new
                {
                    Running = running,
                },
                NetworkSettings = new
                {
                    Ports = ports,
                },
                Mounts = mounts,
            }
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }
}
