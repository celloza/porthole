using System.IO.Pipes;
using System.Net;
using System.Text;
using System.Text.Json;
using Porthole.Core.Models;

namespace Porthole.Tray.Services;

internal sealed class DockerApiServer(IDockerApiBackend backendService, DockerApiConfiguration configuration) : IDisposable
{
    private const string LogEnabledEnvironmentVariable = "PORTHOLE_DOCKER_API_LOG";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = null,
        WriteIndented = false,
    };

    private static readonly string DiagnosticsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Porthole");

    private static readonly string RequestLogPath = Path.Combine(DiagnosticsDirectory, "docker-api.log");

    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _httpLoop;
    private Task[] _pipeLoops = [];
    private readonly string _httpPrefix = Environment.GetEnvironmentVariable("PORTHOLE_DOCKER_API_URL")
        ?? NormalizePrefix(configuration.HttpUrl);
    private readonly string[] _pipeNames = GetConfiguredPipeNames(configuration);
    private readonly bool _requestLoggingEnabled = IsLoggingEnabled(configuration);

    public void Start()
    {
        if (_httpLoop is not null || _pipeLoops.Length > 0)
        {
            return;
        }

        Log($"Docker API server starting. HttpPrefix={_httpPrefix}; Pipes=[{string.Join(", ", _pipeNames)}]");
        _listener.Prefixes.Add(_httpPrefix);
        _listener.Start();
        _httpLoop = Task.Run(() => RunHttpListenerAsync(_shutdown.Token));
        _pipeLoops = _pipeNames
            .Select(pipeName => Task.Run(() => RunPipeListenerAsync(pipeName, _shutdown.Token)))
            .ToArray();
    }

    public void Dispose()
    {
        Log("Docker API server dispose requested.");
        _shutdown.Cancel();

        if (_listener.IsListening)
        {
            _listener.Stop();
        }

        _listener.Close();

        WaitForLoop(_httpLoop);
        foreach (Task pipeLoop in _pipeLoops)
        {
            WaitForLoop(pipeLoop);
        }
    }

    private static string[] GetConfiguredPipeNames(DockerApiConfiguration configuration)
    {
        string? overridePipeName = Environment.GetEnvironmentVariable("PORTHOLE_DOCKER_API_PIPE");
        if (!string.IsNullOrWhiteSpace(overridePipeName))
        {
            return [overridePipeName.Trim()];
        }

        return configuration.PipeNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsLoggingEnabled(DockerApiConfiguration configuration)
    {
        string? value = Environment.GetEnvironmentVariable(LogEnabledEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value.Equals("1", StringComparison.OrdinalIgnoreCase)
                || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
                || value.Equals("on", StringComparison.OrdinalIgnoreCase);
        }

        return configuration.RequestLoggingEnabled;
    }

    private static void WaitForLoop(Task? loop)
    {
        try
        {
            loop?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (HttpListenerException)
        {
        }
        catch (IOException)
        {
        }
    }

    private async Task RunHttpListenerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                HttpListenerContext context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleHttpContextAsync(context, cancellationToken), cancellationToken);
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task RunPipeListenerAsync(string pipeName, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var server = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            try
            {
                Log($"Waiting for Docker API pipe client on '{pipeName}'.");
                await server.WaitForConnectionAsync(cancellationToken);
                Log($"Docker API pipe client connected on '{pipeName}'.");
                _ = Task.Run(() => HandlePipeConnectionAsync(server, pipeName, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                server.Dispose();
                return;
            }
            catch
            {
                server.Dispose();
                throw;
            }
        }
    }

    private async Task HandleHttpContextAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            var request = new DockerApiRequest(
                "http",
                null,
                context.Request.HttpMethod.ToUpperInvariant(),
                NormalizePath(context.Request.Url?.AbsolutePath ?? "/"),
                ParseQueryParameters(context.Request.Url?.Query),
                await ReadBodyAsync(context.Request, cancellationToken));

            LogRequest(request);
            if (TryCreateStreamingLogsRequest(request, out ContainerLogsRequest? logsRequest) && logsRequest is not null)
            {
                DockerApiResponse streamingResponse = CreateEmptyResponse(HttpStatusCode.OK);
                LogResponse(request, streamingResponse);
                await WriteStreamingHttpLogsResponseAsync(context.Response, logsRequest, cancellationToken);
                return;
            }

            DockerApiResponse response = await HandleRequestAsync(request, cancellationToken);
            LogResponse(request, response);
            await WriteHttpResponseAsync(context.Response, response, cancellationToken);
        }
        catch (Exception ex)
        {
            Log($"HTTP Docker API request failed: {ex}");
            DockerApiResponse error = CreateJsonResponse(HttpStatusCode.InternalServerError, new { message = ex.Message });
            await WriteHttpResponseAsync(context.Response, error, cancellationToken);
        }
    }

    private async Task HandlePipeConnectionAsync(NamedPipeServerStream stream, string pipeName, CancellationToken cancellationToken)
    {
        try
        {
            DockerApiRequest request = await ReadPipeRequestAsync(stream, pipeName, cancellationToken);
            LogRequest(request);
            if (TryCreateStreamingLogsRequest(request, out ContainerLogsRequest? logsRequest) && logsRequest is not null)
            {
                DockerApiResponse streamingResponse = CreateEmptyResponse(HttpStatusCode.OK);
                LogResponse(request, streamingResponse);
                await WriteStreamingPipeLogsResponseAsync(stream, logsRequest, cancellationToken);
                return;
            }

            DockerApiResponse response = await HandleRequestAsync(request, cancellationToken);
            LogResponse(request, response);
            await WritePipeResponseAsync(stream, response, cancellationToken);
        }
        catch (Exception ex)
        {
            Log($"Named-pipe Docker API request failed: {ex}");
            DockerApiResponse error = CreateJsonResponse(HttpStatusCode.InternalServerError, new { message = ex.Message });
            await WritePipeResponseAsync(stream, error, cancellationToken);
        }
        finally
        {
            stream.Dispose();
        }
    }

    private async Task<DockerApiResponse> HandleRequestAsync(DockerApiRequest request, CancellationToken cancellationToken)
    {
        string path = request.Path;
        string method = request.Method;

        if (path == "/_ping" && method == "GET")
        {
            return CreateTextResponse(HttpStatusCode.OK, "OK", "text/plain");
        }

        if (path == "/_ping" && method == "HEAD")
        {
            return CreateEmptyResponse(HttpStatusCode.OK, "text/plain");
        }

        if (path == "/version" && method == "GET")
        {
            return CreateJsonResponse(HttpStatusCode.OK, new
            {
                Version = "24.0.0",
                ApiVersion = "1.43",
                MinAPIVersion = "1.12",
                Os = "linux",
                Arch = "amd64",
            });
        }

        if (path == "/info" && method == "GET")
        {
            IReadOnlyList<ContainerSummary> containers = await backendService.ListContainersAsync(cancellationToken);
            IReadOnlyList<ImageSummary> images = await backendService.ListImagesAsync(cancellationToken);

            return CreateJsonResponse(HttpStatusCode.OK, new
            {
                ID = "porthole",
                Containers = containers.Count,
                ContainersRunning = containers.Count(c => c.IsRunning),
                ContainersStopped = containers.Count(c => !c.IsRunning),
                Images = images.Count,
                Driver = "wslc",
                OperatingSystem = "Porthole / WSL Containers",
                OSType = "linux",
                Architecture = Environment.Is64BitProcess ? "x86_64" : "x86",
                ServerVersion = "24.0.0",
            });
        }

        if (path == "/networks" && method == "GET")
        {
            NetworkingSnapshot snapshot = await backendService.GetNetworkingSnapshotAsync(cancellationToken);
            DateTimeOffset createdAt = backendService.GetActiveSessionCreatedAtUtc();
            var payload = new[]
            {
                new
                {
                    Name = "bridge",
                    Id = "porthole-bridge",
                    Created = createdAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'"),
                    Driver = "bridge",
                    Scope = "local",
                    Internal = false,
                    EnableIPv6 = false,
                    Ingress = false,
                    Labels = new Dictionary<string, string>(),
                    Options = new Dictionary<string, string>
                    {
                        ["com.porthole.networkMode"] = snapshot.ActiveMode.ToString(),
                    },
                }
            };

            return CreateJsonResponse(HttpStatusCode.OK, payload);
        }

        if (path == "/images/json" && method == "GET")
        {
            IReadOnlyList<ImageSummary> images = await backendService.ListImagesAsync(cancellationToken);
            var payload = new List<object>(images.Count);
            foreach (ImageSummary image in images)
            {
                var details = await backendService.GetImageDetailsAsync(image.Reference, cancellationToken);
                payload.Add(new
                {
                    Id = image.Id,
                    RepoTags = new[] { image.Reference },
                    Created = details.CreatedAt.ToUnixTimeSeconds(),
                    Size = details.Size,
                });
            }

            return CreateJsonResponse(HttpStatusCode.OK, payload);
        }

        if (TryMatch(path, "/images/", "/json", out string? imageReference) && method == "GET")
        {
            var image = await backendService.GetImageDetailsAsync(Uri.UnescapeDataString(imageReference!), cancellationToken);
            var payload = new
            {
                Id = image.Id,
                RepoTags = new[] { image.Reference },
                RepoDigests = new[] { image.Id },
                Parent = string.Empty,
                Comment = string.Empty,
                Created = image.CreatedAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'"),
                Container = string.Empty,
                ContainerConfig = new { },
                DockerVersion = string.Empty,
                Author = string.Empty,
                Config = new { },
                Architecture = "amd64",
                Os = "linux",
                Size = image.Size,
                VirtualSize = image.Size,
                GraphDriver = new
                {
                    Name = "wslc",
                    Data = new Dictionary<string, string>(),
                },
                RootFS = new
                {
                    Type = "layers",
                    Layers = Array.Empty<string>(),
                },
                Metadata = new
                {
                    LastTagTime = image.CreatedAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'"),
                },
            };

            return CreateJsonResponse(HttpStatusCode.OK, payload);
        }

        if (path == "/volumes" && method == "GET")
        {
            IReadOnlyList<VolumeSummary> volumes = await backendService.ListVolumesAsync(cancellationToken);
            var payload = new
            {
                Volumes = volumes.Select(volume => new
                {
                    Name = volume.Name,
                    Driver = string.IsNullOrWhiteSpace(volume.Driver) ? "local" : volume.Driver,
                    Mountpoint = volume.MountPoint,
                    CreatedAt = (volume.CreatedAtUtc ?? backendService.GetActiveSessionCreatedAtUtc()).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'"),
                    Labels = new Dictionary<string, string>(),
                    Scope = "local",
                    Options = new Dictionary<string, string?>
                    {
                        ["hostPath"] = volume.HostPath,
                        ["sessionMountPoint"] = volume.SessionMountPoint,
                        ["size"] = volume.SizeLabel,
                        ["inUse"] = volume.IsInUse ? "true" : "false",
                    },
                }),
                Warnings = Array.Empty<string>(),
            };

            return CreateJsonResponse(HttpStatusCode.OK, payload);
        }

        if (path == "/containers/json" && method == "GET")
        {
            IReadOnlyList<ContainerSummary> containers = await backendService.ListContainersAsync(cancellationToken);
            bool includeAll = TryGetQueryValue(request.Query, "all", out string? allValue)
                && (string.Equals(allValue, "1", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(allValue, "true", StringComparison.OrdinalIgnoreCase));

            var payload = containers
                .Where(container => includeAll || container.IsRunning)
                .Select(container => new
                {
                    Id = container.Id,
                    Names = new[] { "/" + container.DisplayName },
                    Image = container.Image,
                    ImageID = container.Image,
                    Command = string.Empty,
                    Created = container.CreatedAtUtc.ToUnixTimeSeconds(),
                    State = container.IsRunning ? "running" : "exited",
                    Status = container.StateText.ToLowerInvariant(),
                    Ports = Array.Empty<object>(),
                    Labels = new Dictionary<string, string>(),
                });

            return CreateJsonResponse(HttpStatusCode.OK, payload);
        }

        if (TryMatch(path, "/containers/", "/json", out string? inspectId) && method == "GET")
        {
            string inspectJson = await backendService.InspectContainerJsonAsync(inspectId!, cancellationToken);
            return CreateRawJsonResponse(HttpStatusCode.OK, inspectJson);
        }

        if (TryMatch(path, "/containers/", "/logs", out string? logsId) && method == "GET")
        {
            string? tail = TryGetQueryValue(request.Query, "tail", out string? tailValue) ? tailValue : null;
            bool timestamps = TryGetQueryValue(request.Query, "timestamps", out string? timestampsValue)
                && (string.Equals(timestampsValue, "1", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(timestampsValue, "true", StringComparison.OrdinalIgnoreCase));
            string? since = TryGetQueryValue(request.Query, "since", out string? sinceValue) ? sinceValue : null;
            string? until = TryGetQueryValue(request.Query, "until", out string? untilValue) ? untilValue : null;

            string logs = await backendService.GetContainerLogsAsync(logsId!, tail, timestamps, since, until, cancellationToken);
            byte[] encodedLogs = EncodeDockerRawStream(logs);
            return CreateBinaryResponse(HttpStatusCode.OK, encodedLogs, "application/vnd.docker.raw-stream");
        }

        if (path == "/containers/create" && method == "POST")
        {
            var createRequest = JsonSerializer.Deserialize<CreateContainerRequest>(request.Body, JsonOptions)
                ?? throw new InvalidOperationException("Invalid container create request.");

            string? name = TryGetQueryValue(request.Query, "name", out string? requestedName) ? requestedName : null;
            string image = createRequest.Image ?? throw new InvalidOperationException("Image is required.");
            IReadOnlyList<string>? binds = createRequest.HostConfig?.Binds;
            IReadOnlyList<string>? portMappings = FlattenPortBindings(createRequest.HostConfig?.PortBindings);

            string id = await backendService.CreateDockerContainerAsync(
                image,
                name,
                createRequest.Cmd,
                createRequest.Env,
                binds,
                portMappings,
                cancellationToken);

            return CreateJsonResponse(HttpStatusCode.Created, new { Id = id, Warnings = Array.Empty<string>() });
        }

        if (TryMatch(path, "/containers/", "/start", out string? startId) && method == "POST")
        {
            await backendService.StartContainerAsync(startId!, cancellationToken);
            return CreateEmptyResponse(HttpStatusCode.NoContent);
        }

        if (TryMatch(path, "/containers/", "/stop", out string? stopId) && method == "POST")
        {
            await backendService.StopContainerAsync(stopId!, cancellationToken);
            return CreateEmptyResponse(HttpStatusCode.NoContent);
        }

        if (TryMatch(path, "/containers/", string.Empty, out string? deleteId) && method == "DELETE")
        {
            await backendService.RemoveContainerAsync(deleteId!, cancellationToken);
            return CreateEmptyResponse(HttpStatusCode.NoContent);
        }

        if (TryMatch(path, "/containers/", "/exec", out string? execContainerId) && method == "POST")
        {
            var execRequest = JsonSerializer.Deserialize<ExecCreateRequest>(request.Body, JsonOptions)
                ?? throw new InvalidOperationException("Invalid exec request.");
            string execId = Guid.NewGuid().ToString("N");
            ExecRegistry.Store(execId, execContainerId!, execRequest);
            return CreateJsonResponse(HttpStatusCode.Created, new { Id = execId });
        }

        if (TryMatch(path, "/exec/", "/start", out string? execIdToStart) && method == "POST")
        {
            if (!ExecRegistry.TryTake(execIdToStart!, out PendingExecRequest? pending) || pending is null)
            {
                throw new InvalidOperationException("Exec request not found.");
            }

            var result = await backendService.ExecContainerAsync(
                pending.ContainerReference,
                pending.Request.Cmd ?? [],
                pending.Request.WorkingDir,
                pending.Request.Env,
                cancellationToken);

            return CreateTextResponse(HttpStatusCode.OK, result.StandardOutput, "text/plain");
        }

        return CreateJsonResponse(HttpStatusCode.NotFound, new { message = $"Unsupported Docker API endpoint: {method} {path}" });
    }

    private static IReadOnlyList<string>? FlattenPortBindings(Dictionary<string, List<PortBindingRequest>>? bindings)
    {
        if (bindings is null || bindings.Count == 0)
        {
            return null;
        }

        var results = new List<string>();
        foreach ((string containerPortKey, List<PortBindingRequest> values) in bindings)
        {
            string[] parts = containerPortKey.Split('/');
            string port = parts[0];
            string protocol = parts.Length > 1 ? "/" + parts[1] : string.Empty;

            foreach (PortBindingRequest binding in values)
            {
                if (!string.IsNullOrWhiteSpace(binding.HostPort))
                {
                    results.Add($"{binding.HostPort}:{port}{protocol}");
                }
            }
        }

        return results;
    }

    private static string NormalizePrefix(string prefix)
    {
        if (!prefix.EndsWith('/'))
        {
            prefix += "/";
        }

        return prefix;
    }

    private static string NormalizePath(string path)
    {
        if (path.StartsWith("/v", StringComparison.OrdinalIgnoreCase))
        {
            int slash = path.IndexOf('/', 2);
            if (slash > 0)
            {
                return path[slash..];
            }
        }

        return path;
    }

    private static bool TryMatch(string path, string prefix, string suffix, out string? value)
    {
        value = null;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string remainder = path[prefix.Length..];
        if (!string.IsNullOrEmpty(suffix))
        {
            if (!remainder.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            remainder = remainder[..^suffix.Length];
        }

        if (string.IsNullOrWhiteSpace(remainder) || remainder.Contains('/'))
        {
            return false;
        }

        value = remainder;
        return true;
    }

    private static async Task<string> ReadBodyAsync(HttpListenerRequest request, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8, leaveOpen: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static async Task<DockerApiRequest> ReadPipeRequestAsync(Stream stream, string pipeName, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);

        string requestLine = await ReadRequiredLineAsync(reader, cancellationToken);
        string[] requestLineParts = requestLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (requestLineParts.Length < 2)
        {
            throw new InvalidOperationException("Invalid HTTP request line.");
        }

        string method = requestLineParts[0].ToUpperInvariant();
        string rawTarget = requestLineParts[1];
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        while (true)
        {
            string line = await ReadRequiredLineAsync(reader, cancellationToken);
            if (line.Length == 0)
            {
                break;
            }

            int separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            string name = line[..separator].Trim();
            string value = line[(separator + 1)..].Trim();
            headers[name] = value;
        }

        int contentLength = 0;
        if (headers.TryGetValue("Content-Length", out string? contentLengthValue)
            && !int.TryParse(contentLengthValue, out contentLength))
        {
            throw new InvalidOperationException("Invalid Content-Length header.");
        }

        string body = string.Empty;
        if (contentLength > 0)
        {
            char[] bodyBuffer = new char[contentLength];
            int offset = 0;
            while (offset < contentLength)
            {
                int read = await reader.ReadAsync(bodyBuffer.AsMemory(offset, contentLength - offset), cancellationToken);
                if (read == 0)
                {
                    throw new EndOfStreamException("Unexpected end of stream while reading request body.");
                }

                offset += read;
            }

            body = new string(bodyBuffer);
        }

        string path = rawTarget;
        string? queryString = null;
        int queryIndex = rawTarget.IndexOf('?');
        if (queryIndex >= 0)
        {
            path = rawTarget[..queryIndex];
            queryString = rawTarget[(queryIndex + 1)..];
        }

        return new DockerApiRequest("npipe", pipeName, method, NormalizePath(path), ParseQueryParameters(queryString), body);
    }

    private static async Task<string> ReadRequiredLineAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        string? line = await reader.ReadLineAsync(cancellationToken);
        return line ?? throw new EndOfStreamException("Unexpected end of stream while reading HTTP headers.");
    }

    private static Task WriteHttpResponseAsync(HttpListenerResponse response, DockerApiResponse payload, CancellationToken cancellationToken)
    {
        return WriteBytesAsync(response, payload.StatusCode, payload.Body, payload.ContentType, cancellationToken);
    }

    private static async Task WritePipeResponseAsync(Stream stream, DockerApiResponse response, CancellationToken cancellationToken)
    {
        byte[] body = response.Body;
        string headers = string.Join("\r\n", new[]
        {
            $"HTTP/1.1 {(int)response.StatusCode} {GetReasonPhrase(response.StatusCode)}",
            $"Content-Type: {response.ContentType}",
            $"Content-Length: {body.Length}",
            "Connection: close",
            string.Empty,
            string.Empty,
        });

        byte[] headerBytes = Encoding.ASCII.GetBytes(headers);
        await stream.WriteAsync(headerBytes, cancellationToken);
        if (body.Length > 0)
        {
            await stream.WriteAsync(body, cancellationToken);
        }

        await stream.FlushAsync(cancellationToken);
    }

    private async Task WriteStreamingHttpLogsResponseAsync(HttpListenerResponse response, ContainerLogsRequest request, CancellationToken cancellationToken)
    {
        response.StatusCode = (int)HttpStatusCode.OK;
        response.ContentType = "application/vnd.docker.raw-stream";
        response.SendChunked = true;
        response.KeepAlive = false;

        try
        {
            await backendService.StreamContainerLogsAsync(
                request.ContainerId,
                new DockerRawStreamWriterStream(response.OutputStream),
                request.Tail,
                request.Timestamps,
                request.Since,
                request.Until,
                request.Follow,
                cancellationToken);
        }
        finally
        {
            response.Close();
        }
    }

    private async Task WriteStreamingPipeLogsResponseAsync(Stream stream, ContainerLogsRequest request, CancellationToken cancellationToken)
    {
        string headers = string.Join("\r\n", new[]
        {
            $"HTTP/1.1 {(int)HttpStatusCode.OK} {GetReasonPhrase(HttpStatusCode.OK)}",
            "Content-Type: application/vnd.docker.raw-stream",
            "Connection: close",
            string.Empty,
            string.Empty,
        });

        byte[] headerBytes = Encoding.ASCII.GetBytes(headers);
        await stream.WriteAsync(headerBytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);

        await backendService.StreamContainerLogsAsync(
            request.ContainerId,
            new DockerRawStreamWriterStream(stream),
            request.Tail,
            request.Timestamps,
            request.Since,
            request.Until,
            request.Follow,
            cancellationToken);
    }

    private static byte[] EncodeDockerRawStream(string payload)
    {
        if (string.IsNullOrEmpty(payload))
        {
            return [];
        }

        byte[] body = Encoding.UTF8.GetBytes(payload);
        return DockerRawStreamWriterStream.Frame(body);
    }

    private static DockerApiResponse CreateJsonResponse(HttpStatusCode statusCode, object payload)
    {
        string json = JsonSerializer.Serialize(payload, JsonOptions);
        return CreateTextResponse(statusCode, json, "application/json");
    }

    private static DockerApiResponse CreateRawJsonResponse(HttpStatusCode statusCode, string payload)
    {
        return CreateTextResponse(statusCode, payload, "application/json");
    }

    private static DockerApiResponse CreateTextResponse(HttpStatusCode statusCode, string payload, string contentType)
    {
        return new DockerApiResponse(statusCode, contentType, Encoding.UTF8.GetBytes(payload));
    }

    private static DockerApiResponse CreateBinaryResponse(HttpStatusCode statusCode, byte[] payload, string contentType)
    {
        return new DockerApiResponse(statusCode, contentType, payload);
    }

    private static DockerApiResponse CreateEmptyResponse(HttpStatusCode statusCode, string contentType = "text/plain")
    {
        return new DockerApiResponse(statusCode, contentType, []);
    }

    private static async Task WriteBytesAsync(HttpListenerResponse response, HttpStatusCode statusCode, byte[] payload, string contentType, CancellationToken cancellationToken)
    {
        response.StatusCode = (int)statusCode;
        response.ContentType = contentType;
        response.ContentLength64 = payload.Length;
        if (payload.Length > 0)
        {
            await response.OutputStream.WriteAsync(payload, cancellationToken);
        }

        response.Close();
    }

    private static Dictionary<string, string> ParseQueryParameters(string? query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query))
        {
            return result;
        }

        string normalized = query[0] == '?' ? query[1..] : query;
        foreach (string pair in normalized.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = pair.IndexOf('=');
            if (separator < 0)
            {
                result[Uri.UnescapeDataString(pair)] = string.Empty;
                continue;
            }

            string name = Uri.UnescapeDataString(pair[..separator]);
            string value = Uri.UnescapeDataString(pair[(separator + 1)..]);
            result[name] = value;
        }

        return result;
    }

    private static bool TryGetQueryValue(IReadOnlyDictionary<string, string> query, string key, out string? value)
    {
        if (query.TryGetValue(key, out string? found))
        {
            value = found;
            return true;
        }

        value = null;
        return false;
    }

    private static bool TryCreateStreamingLogsRequest(DockerApiRequest request, out ContainerLogsRequest? logsRequest)
    {
        logsRequest = null;
        if (!string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase)
            || !TryMatch(request.Path, "/containers/", "/logs", out string? logsId))
        {
            return false;
        }

        bool follow = TryGetQueryValue(request.Query, "follow", out string? followValue)
            && (string.Equals(followValue, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(followValue, "true", StringComparison.OrdinalIgnoreCase));
        if (!follow)
        {
            return false;
        }

        string? tail = TryGetQueryValue(request.Query, "tail", out string? tailValue) ? tailValue : null;
        bool timestamps = TryGetQueryValue(request.Query, "timestamps", out string? timestampsValue)
            && (string.Equals(timestampsValue, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(timestampsValue, "true", StringComparison.OrdinalIgnoreCase));
        string? since = TryGetQueryValue(request.Query, "since", out string? sinceValue) ? sinceValue : null;
        string? until = TryGetQueryValue(request.Query, "until", out string? untilValue) ? untilValue : null;

        logsRequest = new ContainerLogsRequest(logsId!, tail, timestamps, since, until, follow);
        return true;
    }

    private static string GetReasonPhrase(HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            HttpStatusCode.OK => "OK",
            HttpStatusCode.Created => "Created",
            HttpStatusCode.NoContent => "No Content",
            HttpStatusCode.NotFound => "Not Found",
            HttpStatusCode.InternalServerError => "Internal Server Error",
            _ => statusCode.ToString(),
        };
    }

    private void LogRequest(DockerApiRequest request)
    {
        string query = request.Query.Count == 0
            ? string.Empty
            : "?" + string.Join("&", request.Query.Select(pair => $"{pair.Key}={pair.Value}"));
        string transport = request.Transport == "npipe" && !string.IsNullOrWhiteSpace(request.Endpoint)
            ? $"npipe:{request.Endpoint}"
            : request.Transport;
        string bodyPreview = string.IsNullOrWhiteSpace(request.Body)
            ? string.Empty
            : $" body={TruncateForLog(request.Body, 400)}";
        Log($"REQUEST {transport} {request.Method} {request.Path}{query}{bodyPreview}");
    }

    private void LogResponse(DockerApiRequest request, DockerApiResponse response)
    {
        string transport = request.Transport == "npipe" && !string.IsNullOrWhiteSpace(request.Endpoint)
            ? $"npipe:{request.Endpoint}"
            : request.Transport;
        Log($"RESPONSE {transport} {request.Method} {request.Path} {(int)response.StatusCode} {GetReasonPhrase(response.StatusCode)}");
    }

    private void Log(string message)
    {
        if (!_requestLoggingEnabled)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(DiagnosticsDirectory);

            const long maxSizeBytes = 512 * 1024;
            if (File.Exists(RequestLogPath))
            {
                var info = new FileInfo(RequestLogPath);
                if (info.Length > maxSizeBytes)
                {
                    string archivedPath = RequestLogPath + ".1";
                    if (File.Exists(archivedPath))
                    {
                        File.Delete(archivedPath);
                    }

                    File.Move(RequestLogPath, archivedPath);
                }
            }

            File.AppendAllText(RequestLogPath, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    private static string TruncateForLog(string value, int maxLength)
    {
        string singleLine = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (singleLine.Length <= maxLength)
        {
            return singleLine;
        }

        return singleLine[..maxLength] + "...";
    }

    private sealed record DockerApiRequest(
        string Transport,
        string? Endpoint,
        string Method,
        string Path,
        IReadOnlyDictionary<string, string> Query,
        string Body);

    private sealed record DockerApiResponse(
        HttpStatusCode StatusCode,
        string ContentType,
        byte[] Body);

    private sealed record ContainerLogsRequest(
        string ContainerId,
        string? Tail,
        bool Timestamps,
        string? Since,
        string? Until,
        bool Follow);

    private sealed class DockerRawStreamWriterStream(Stream innerStream) : Stream
    {
        private const byte StdoutStream = 1;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => innerStream.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => innerStream.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) => innerStream.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            innerStream.Write(Frame(buffer.AsSpan(offset, count).ToArray()));
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            byte[] framed = Frame(buffer.ToArray());
            await innerStream.WriteAsync(framed, cancellationToken);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        public static byte[] Frame(byte[] payload)
        {
            if (payload.Length == 0)
            {
                return [];
            }

            byte[] framed = new byte[payload.Length + 8];
            framed[0] = StdoutStream;
            framed[4] = (byte)((payload.Length >> 24) & 0xff);
            framed[5] = (byte)((payload.Length >> 16) & 0xff);
            framed[6] = (byte)((payload.Length >> 8) & 0xff);
            framed[7] = (byte)(payload.Length & 0xff);
            payload.CopyTo(framed, 8);
            return framed;
        }
    }

    private sealed record CreateContainerRequest(
        string? Image,
        List<string>? Cmd,
        List<string>? Env,
        CreateContainerHostConfig? HostConfig);

    private sealed record CreateContainerHostConfig(
        List<string>? Binds,
        Dictionary<string, List<PortBindingRequest>>? PortBindings);

    private sealed record PortBindingRequest(string? HostIp, string? HostPort);

    private sealed record ExecCreateRequest(
        List<string>? Cmd,
        string? WorkingDir,
        List<string>? Env);

    private sealed record PendingExecRequest(string ContainerReference, ExecCreateRequest Request);

    private static class ExecRegistry
    {
        private static readonly object Sync = new();
        private static readonly Dictionary<string, PendingExecRequest> Entries = new(StringComparer.OrdinalIgnoreCase);

        public static void Store(string execId, string containerReference, ExecCreateRequest request)
        {
            lock (Sync)
            {
                Entries[execId] = new PendingExecRequest(containerReference, request);
            }
        }

        public static bool TryTake(string execId, out PendingExecRequest? request)
        {
            lock (Sync)
            {
                if (Entries.TryGetValue(execId, out PendingExecRequest? found))
                {
                    Entries.Remove(execId);
                    request = found;
                    return true;
                }
            }

            request = null;
            return false;
        }
    }
}
