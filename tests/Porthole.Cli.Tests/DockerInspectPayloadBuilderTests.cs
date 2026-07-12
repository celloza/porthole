using System.Text.Json;
using porthole_cli.Commands;

namespace Porthole.Cli.Tests;

public class DockerInspectPayloadBuilderTests
{
    [Fact]
    public void Build_MapsRequiredFields_ForRunningContainer()
    {
        const string inspectJson = """
        {
          "Id": "abc123",
          "State": 2,
          "Ports": {
            "80/tcp": [
              { "HostPort": "8080" }
            ]
          },
          "Mounts": [
            {
              "Type": "bind",
              "Source": "C:\\\\repo",
              "Destination": "/workspace"
            }
          ]
        }
        """;

        string payload = DockerInspectPayloadBuilder.Build(inspectJson);
        using var doc = JsonDocument.Parse(payload);

        JsonElement root = doc.RootElement;
        Assert.Equal(JsonValueKind.Array, root.ValueKind);
        Assert.Equal(1, root.GetArrayLength());

        JsonElement first = root[0];
        Assert.Equal("abc123", first.GetProperty("Id").GetString());
        Assert.True(first.GetProperty("State").GetProperty("Running").GetBoolean());
        Assert.Equal(JsonValueKind.Object, first.GetProperty("NetworkSettings").GetProperty("Ports").ValueKind);
        Assert.Equal(JsonValueKind.Array, first.GetProperty("Mounts").ValueKind);
    }

    [Fact]
    public void Build_MissingOptionalFields_UsesSafeDefaults()
    {
        const string inspectJson = """
        {
          "Id": "def456",
          "State": 1
        }
        """;

        string payload = DockerInspectPayloadBuilder.Build(inspectJson);
        using var doc = JsonDocument.Parse(payload);

        JsonElement first = doc.RootElement[0];
        Assert.Equal("def456", first.GetProperty("Id").GetString());
        Assert.False(first.GetProperty("State").GetProperty("Running").GetBoolean());
        Assert.Equal(JsonValueKind.Object, first.GetProperty("NetworkSettings").GetProperty("Ports").ValueKind);
        Assert.Equal(JsonValueKind.Array, first.GetProperty("Mounts").ValueKind);
        Assert.Equal(0, first.GetProperty("Mounts").GetArrayLength());
    }
}
