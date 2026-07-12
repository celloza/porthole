using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Porthole.Core.Models;
using Porthole.Tray.Services;

namespace Porthole.Tray.Tests;

public sealed class DockerApiServerTests
{
    [Fact]
    public async Task NamedPipe_GetNetworks_ReturnsCreatedTimestampAndMode()
    {
        var backend = new FakeDockerApiBackend
        {
            NetworkingSnapshot = new NetworkingSnapshot(
                NetworkMode.Consomme,
                [],
                new ProxyConfiguration(null, null, null)),
            ActiveSessionCreatedAtUtc = DateTimeOffset.Parse("2026-07-11T12:34:56Z"),
        };

        await using var host = await DockerApiServerHarness.StartAsync(backend);

        PipeHttpResponse response = await host.SendAsync(HttpRequest("GET", "/v1.52/networks"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = JsonDocument.Parse(response.BodyText);
        JsonElement network = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal("bridge", network.GetProperty("Name").GetString());
        Assert.Equal("2026-07-11T12:34:56.0000000Z", network.GetProperty("Created").GetString());
        Assert.Equal("Consomme", network.GetProperty("Options").GetProperty("com.porthole.networkMode").GetString());
    }

    [Fact]
    public async Task NamedPipe_GetVolumes_ReturnsCreatedAtAndOptions()
    {
        var backend = new FakeDockerApiBackend
        {
            ActiveSessionCreatedAtUtc = DateTimeOffset.Parse("2026-07-11T12:00:00Z"),
        };
        backend.Volumes.Add(new VolumeSummary(
            Name: "named-data",
            Driver: "local",
            MountPoint: "/var/lib/data",
            HostPath: null,
            SizeLabel: "64 MB",
            IsInUse: true,
            SessionMountPoint: "/session/named-data",
            CreatedAtUtc: DateTimeOffset.Parse("2026-07-10T10:00:00Z")));
        backend.Volumes.Add(new VolumeSummary(
            Name: "bind-src",
            Driver: string.Empty,
            MountPoint: "/workspace",
            HostPath: "C:/src",
            SizeLabel: "12 MB",
            IsInUse: false,
            IsBindMount: true,
            SessionMountPoint: "/session/workspace",
            CreatedAtUtc: null));

        await using var host = await DockerApiServerHarness.StartAsync(backend);

        PipeHttpResponse response = await host.SendAsync(HttpRequest("GET", "/v1.52/volumes"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = JsonDocument.Parse(response.BodyText);
        JsonElement root = document.RootElement;
        Assert.Equal(JsonValueKind.Array, root.GetProperty("Volumes").ValueKind);
        JsonElement[] volumes = root.GetProperty("Volumes").EnumerateArray().ToArray();
        Assert.Equal(2, volumes.Length);

        Assert.Equal("2026-07-10T10:00:00.0000000Z", volumes[0].GetProperty("CreatedAt").GetString());
        Assert.Equal("/session/named-data", volumes[0].GetProperty("Options").GetProperty("sessionMountPoint").GetString());
        Assert.Equal("64 MB", volumes[0].GetProperty("Options").GetProperty("size").GetString());
        Assert.Equal("true", volumes[0].GetProperty("Options").GetProperty("inUse").GetString());

        Assert.Equal("local", volumes[1].GetProperty("Driver").GetString());
        Assert.Equal("2026-07-11T12:00:00.0000000Z", volumes[1].GetProperty("CreatedAt").GetString());
        Assert.Equal("C:/src", volumes[1].GetProperty("Options").GetProperty("hostPath").GetString());
        Assert.Equal("false", volumes[1].GetProperty("Options").GetProperty("inUse").GetString());
    }

    [Fact]
    public async Task NamedPipe_GetImagesJson_ReturnsDockerPayload()
    {
        var backend = new FakeDockerApiBackend();
        backend.Images.Add(new ImageSummary(
            "sha256:d529dd0c6e5597ac7e4a3e2dea65c3fcc6173f4cae713c409265c1dd9914a11b",
            "alpine",
            "latest",
            "25 days ago",
            "8.02 MB",
            "alpine:latest",
            DateTimeOffset.Parse("2026-06-16T00:01:29Z"),
            8415579));
        backend.ImageDetails["alpine:latest"] = new WslcBackendService.DockerImageDetails(
            "sha256:d529dd0c6e5597ac7e4a3e2dea65c3fcc6173f4cae713c409265c1dd9914a11b",
            "alpine",
            "latest",
            "alpine:latest",
            DateTimeOffset.Parse("2026-06-16T00:01:29Z"),
            8415579);

        await using var host = await DockerApiServerHarness.StartAsync(backend);

        PipeHttpResponse response = await host.SendAsync("GET /v1.52/images/json?filters={\"dangling\":{\"false\":true}} HTTP/1.1\r\nHost: docker\r\n\r\n");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = JsonDocument.Parse(response.BodyText);
        JsonElement image = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal("sha256:d529dd0c6e5597ac7e4a3e2dea65c3fcc6173f4cae713c409265c1dd9914a11b", image.GetProperty("Id").GetString());
        Assert.Equal("alpine:latest", Assert.Single(image.GetProperty("RepoTags").EnumerateArray()).GetString());
        Assert.Equal(1781568089L, image.GetProperty("Created").GetInt64());
        Assert.Equal(8415579L, image.GetProperty("Size").GetInt64());
        Assert.Empty(backend.ImageDetailRequests);
    }

    [Fact]
    public async Task NamedPipe_GetImageJson_ReturnsDockerInspectPayload()
    {
        var backend = new FakeDockerApiBackend();
        backend.ImageDetails["sha256:d529dd0c6e5597ac7e4a3e2dea65c3fcc6173f4cae713c409265c1dd9914a11b"] = new WslcBackendService.DockerImageDetails(
            "sha256:d529dd0c6e5597ac7e4a3e2dea65c3fcc6173f4cae713c409265c1dd9914a11b",
            "alpine",
            "latest",
            "alpine:latest",
            DateTimeOffset.Parse("2026-06-16T00:01:29Z"),
            8415579);

        await using var host = await DockerApiServerHarness.StartAsync(backend);

        PipeHttpResponse response = await host.SendAsync("GET /v1.52/images/sha256%3Ad529dd0c6e5597ac7e4a3e2dea65c3fcc6173f4cae713c409265c1dd9914a11b/json HTTP/1.1\r\nHost: docker\r\n\r\n");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = JsonDocument.Parse(response.BodyText);
        Assert.Equal("sha256:d529dd0c6e5597ac7e4a3e2dea65c3fcc6173f4cae713c409265c1dd9914a11b", document.RootElement.GetProperty("Id").GetString());
        Assert.Equal("linux", document.RootElement.GetProperty("Os").GetString());
        Assert.Equal("amd64", document.RootElement.GetProperty("Architecture").GetString());
        Assert.Equal("2026-06-16T00:01:29.0000000Z", document.RootElement.GetProperty("Created").GetString());
        Assert.Equal("sha256:d529dd0c6e5597ac7e4a3e2dea65c3fcc6173f4cae713c409265c1dd9914a11b", Assert.Single(document.RootElement.GetProperty("RepoDigests").EnumerateArray()).GetString());
    }

    [Fact]
    public async Task NamedPipe_HeadPing_ReturnsOk()
    {
        var backend = new FakeDockerApiBackend();
        await using var host = await DockerApiServerHarness.StartAsync(backend);

        PipeHttpResponse response = await host.SendAsync(HttpRequest("HEAD", "/v1.52/_ping"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(response.Body);
    }

    [Fact]
    public async Task NamedPipe_GetContainerLogs_ReturnsDockerRawStreamAndForwardsQuery()
    {
        var backend = new FakeDockerApiBackend
        {
            ContainerLogsOutput = "porthole-live-log\n",
        };

        await using var host = await DockerApiServerHarness.StartAsync(backend);

        PipeHttpResponse response = await host.SendAsync(HttpRequest(
            "GET",
            "/v1.52/containers/vscode-probe/logs?tail=10&timestamps=1&since=2026-07-11T11:00:00Z&until=2026-07-11T12:00:00Z"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.docker.raw-stream", response.Headers["Content-Type"]);
        Assert.Equal("porthole-live-log\n", DecodeDockerRawStream(response.Body));

        Assert.NotNull(backend.LastLogsRequest);
        Assert.Equal("vscode-probe", backend.LastLogsRequest!.ContainerReference);
        Assert.Equal("10", backend.LastLogsRequest.Tail);
        Assert.True(backend.LastLogsRequest.Timestamps);
        Assert.Equal("2026-07-11T11:00:00Z", backend.LastLogsRequest.Since);
        Assert.Equal("2026-07-11T12:00:00Z", backend.LastLogsRequest.Until);
    }

    [Fact]
    public async Task NamedPipe_ExecLifecycle_CreatesAndStartsExecRequest()
    {
        var backend = new FakeDockerApiBackend
        {
            ExecResult = new WslcBackendService.DockerExecResult(0, "command output", string.Empty),
        };

        await using var host = await DockerApiServerHarness.StartAsync(backend);

        const string createBody = "{\"Cmd\":[\"sh\",\"-lc\",\"echo hi\"],\"WorkingDir\":\"/workspace\",\"Env\":[\"A=1\",\"B=naïve\"]}";
        PipeHttpResponse createResponse = await host.SendAsync(HttpRequest("POST", "/v1.52/containers/vscode-probe/exec", createBody, "application/json"));

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        using JsonDocument createdDocument = JsonDocument.Parse(createResponse.BodyText);
        string execId = createdDocument.RootElement.GetProperty("Id").GetString()!;
        Assert.False(string.IsNullOrWhiteSpace(execId));

        PipeHttpResponse startResponse = await host.SendAsync(HttpRequest("POST", $"/v1.52/exec/{execId}/start", "{}", "application/json"));

        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);
        Assert.Equal("text/plain", startResponse.Headers["Content-Type"]);
        Assert.Equal("command output", startResponse.BodyText);

        Assert.NotNull(backend.LastExecRequest);
        Assert.Equal("vscode-probe", backend.LastExecRequest!.ContainerReference);
        Assert.Equal("/workspace", backend.LastExecRequest.WorkingDirectory);
        Assert.Equal(new[] { "A=1", "B=naïve" }, backend.LastExecRequest.Environment);
        Assert.Equal(new[] { "sh", "-lc", "echo hi" }, backend.LastExecRequest.Command);
    }

    [Fact]
    public async Task NamedPipe_ExecStart_WithoutPendingExec_ReturnsInternalServerError()
    {
        var backend = new FakeDockerApiBackend();
        await using var host = await DockerApiServerHarness.StartAsync(backend);

        PipeHttpResponse response = await host.SendAsync(HttpRequest("POST", "/v1.52/exec/missing/start", "{}", "application/json"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        using JsonDocument document = JsonDocument.Parse(response.BodyText);
        Assert.Equal("Exec request not found.", document.RootElement.GetProperty("message").GetString());
    }

    private static string HttpRequest(string method, string path, string? body = null, string? contentType = null)
    {
        body ??= string.Empty;

        var builder = new StringBuilder();
        builder.Append(method).Append(' ').Append(path).Append(" HTTP/1.1\r\n");
        builder.Append("Host: docker\r\n");

        if (!string.IsNullOrEmpty(contentType))
        {
            builder.Append("Content-Type: ").Append(contentType).Append("\r\n");
        }

        if (body.Length > 0)
        {
            builder.Append("Content-Length: ").Append(Encoding.UTF8.GetByteCount(body)).Append("\r\n");
        }

        builder.Append("\r\n");
        builder.Append(body);
        return builder.ToString();
    }

    private static string DecodeDockerRawStream(byte[] framed)
    {
        if (framed.Length == 0)
        {
            return string.Empty;
        }

        Assert.True(framed.Length >= 8, "Docker raw-stream payload was shorter than the 8-byte frame header.");
        Assert.Equal(1, framed[0]);

        int length = (framed[4] << 24) | (framed[5] << 16) | (framed[6] << 8) | framed[7];
        Assert.Equal(length + 8, framed.Length);

        return Encoding.UTF8.GetString(framed, 8, length);
    }

    private sealed class FakeDockerApiBackend : IDockerApiBackend
    {
        public List<ImageSummary> Images { get; } = [];

        public List<VolumeSummary> Volumes { get; } = [];

        public Dictionary<string, WslcBackendService.DockerImageDetails> ImageDetails { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> ImageDetailRequests { get; } = [];

        public NetworkingSnapshot NetworkingSnapshot { get; set; } = new(NetworkMode.Bridge, [], new ProxyConfiguration(null, null, null));

        public DateTimeOffset ActiveSessionCreatedAtUtc { get; set; } = DateTimeOffset.Parse("2026-07-11T12:00:00Z");

        public string ContainerLogsOutput { get; set; } = string.Empty;

        public ContainerLogsCall? LastLogsRequest { get; private set; }

        public WslcBackendService.DockerExecResult ExecResult { get; set; } = new(0, string.Empty, string.Empty);

        public ExecCall? LastExecRequest { get; private set; }

        public Task<string> CreateDockerContainerAsync(string image, string? name, IReadOnlyList<string>? command, IReadOnlyList<string>? environment, IReadOnlyList<string>? binds, IReadOnlyList<string>? portMappings, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<WslcBackendService.DockerExecResult> ExecContainerAsync(string containerReference, IReadOnlyList<string> command, string? workingDirectory, IReadOnlyList<string>? environment, CancellationToken cancellationToken = default)
        {
            LastExecRequest = new ExecCall(containerReference, command.ToArray(), workingDirectory, environment?.ToArray() ?? []);
            return Task.FromResult(ExecResult);
        }

        public DateTimeOffset GetActiveSessionCreatedAtUtc() => ActiveSessionCreatedAtUtc;

        public Task<string> GetContainerLogsAsync(string containerReference, string? tail, bool timestamps, string? since, string? until, CancellationToken cancellationToken = default)
        {
            LastLogsRequest = new ContainerLogsCall(containerReference, tail, timestamps, since, until);
            return Task.FromResult(ContainerLogsOutput);
        }

        public Task<WslcBackendService.DockerImageDetails> GetImageDetailsAsync(string imageReference, CancellationToken cancellationToken = default)
        {
            ImageDetailRequests.Add(imageReference);
            if (!ImageDetails.TryGetValue(imageReference, out WslcBackendService.DockerImageDetails? details))
            {
                throw new InvalidOperationException($"Image not found: {imageReference}");
            }

            return Task.FromResult(details);
        }

        public Task<NetworkingSnapshot> GetNetworkingSnapshotAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(NetworkingSnapshot);

        public Task<string> InspectContainerJsonAsync(string containerReference, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<ContainerSummary>> ListContainersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ContainerSummary>>([]);

        public Task<IReadOnlyList<ImageSummary>> ListImagesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ImageSummary>>(Images);

        public Task<IReadOnlyList<VolumeSummary>> ListVolumesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<VolumeSummary>>(Volumes);

        public Task RemoveContainerAsync(string containerReference, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task StartContainerAsync(string containerReference, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task StopContainerAsync(string containerReference, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task StreamContainerLogsAsync(string containerReference, Stream destination, string? tail, bool timestamps, string? since, string? until, bool follow, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public sealed record ContainerLogsCall(string ContainerReference, string? Tail, bool Timestamps, string? Since, string? Until);

        public sealed record ExecCall(string ContainerReference, IReadOnlyList<string> Command, string? WorkingDirectory, IReadOnlyList<string> Environment);
    }

    private sealed class DockerApiServerHarness : IAsyncDisposable
    {
        private readonly DockerApiServer _server;

        private DockerApiServerHarness(DockerApiServer server, string pipeName)
        {
            _server = server;
            PipeName = pipeName;
        }

        public string PipeName { get; }

        public static Task<DockerApiServerHarness> StartAsync(IDockerApiBackend backend)
        {
            string pipeName = $"porthole-test-{Guid.NewGuid():N}";
            int port = GetFreeTcpPort();
            var configuration = new DockerApiConfiguration($"http://127.0.0.1:{port}/", [pipeName], RequestLoggingEnabled: false);
            var server = new DockerApiServer(backend, configuration);
            server.Start();
            return Task.FromResult(new DockerApiServerHarness(server, pipeName));
        }

        public async Task<PipeHttpResponse> SendAsync(string request)
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(5000);

            byte[] requestBytes = Encoding.UTF8.GetBytes(request);
            await client.WriteAsync(requestBytes);
            await client.FlushAsync();

            using var buffer = new MemoryStream();
            byte[] chunk = new byte[4096];
            while (true)
            {
                int read = await client.ReadAsync(chunk);
                if (read == 0)
                {
                    break;
                }

                buffer.Write(chunk, 0, read);
            }

            return PipeHttpResponse.Parse(buffer.ToArray());
        }

        public ValueTask DisposeAsync()
        {
            _server.Dispose();
            return ValueTask.CompletedTask;
        }

        private static int GetFreeTcpPort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
    }

    private sealed record PipeHttpResponse(HttpStatusCode StatusCode, IReadOnlyDictionary<string, string> Headers, byte[] Body)
    {
        public string BodyText => Encoding.UTF8.GetString(Body);

        public static PipeHttpResponse Parse(byte[] payload)
        {
            byte[] separator = Encoding.ASCII.GetBytes("\r\n\r\n");
            int headerEnd = payload.AsSpan().IndexOf(separator);
            Assert.True(headerEnd >= 0, "HTTP response did not contain a header terminator.");

            string headers = Encoding.ASCII.GetString(payload, 0, headerEnd);
            string[] headerLines = headers.Split("\r\n", StringSplitOptions.None);
            string[] statusParts = headerLines[0].Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            Assert.True(statusParts.Length >= 2, "HTTP response did not contain a valid status line.");

            var parsedHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in headerLines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                int colonIndex = line.IndexOf(':');
                if (colonIndex <= 0)
                {
                    continue;
                }

                parsedHeaders[line[..colonIndex].Trim()] = line[(colonIndex + 1)..].Trim();
            }

            byte[] body = payload[(headerEnd + separator.Length)..];
            return new PipeHttpResponse((HttpStatusCode)int.Parse(statusParts[1]), parsedHeaders, body);
        }
    }
}