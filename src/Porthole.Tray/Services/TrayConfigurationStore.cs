using System.Text.Json;

namespace Porthole.Tray.Services;

internal sealed class TrayConfigurationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private static readonly string ConfigurationDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Porthole",
        "Tray");

    private static readonly string ConfigurationPath = Path.Combine(ConfigurationDirectory, "settings.json");

    public TrayConfiguration Load()
    {
        Directory.CreateDirectory(ConfigurationDirectory);

        if (!File.Exists(ConfigurationPath))
        {
            TrayConfiguration defaults = TrayConfiguration.CreateDefault();
            Save(defaults);
            return defaults;
        }

        string json = File.ReadAllText(ConfigurationPath);
        TrayConfiguration? configuration = JsonSerializer.Deserialize<TrayConfiguration>(json, JsonOptions);
        if (configuration is null)
        {
            throw new InvalidOperationException($"Tray configuration '{ConfigurationPath}' is invalid.");
        }

        return configuration.WithDefaultsApplied();
    }

    private static void Save(TrayConfiguration configuration)
    {
        string json = JsonSerializer.Serialize(configuration, JsonOptions);
        File.WriteAllText(ConfigurationPath, json);
    }
}

internal sealed record TrayConfiguration(DockerApiConfiguration DockerApi)
{
    public static TrayConfiguration CreateDefault()
    {
        return new TrayConfiguration(new DockerApiConfiguration(
            HttpUrl: "http://127.0.0.1:23751/",
            PipeNames: ["docker_engine", "dockerDesktopLinuxEngine"],
            RequestLoggingEnabled: true));
    }

    public TrayConfiguration WithDefaultsApplied()
    {
        DockerApiConfiguration defaults = CreateDefault().DockerApi;
        IReadOnlyList<string> configuredPipeNames = DockerApi.PipeNames is { Count: > 0 }
            ? DockerApi.PipeNames.Where(name => !string.IsNullOrWhiteSpace(name)).ToArray()
            : [];

        if (configuredPipeNames.Count == 0 && !string.IsNullOrWhiteSpace(DockerApi.PipeName))
        {
            configuredPipeNames = [DockerApi.PipeName];
        }

        return this with
        {
            DockerApi = new DockerApiConfiguration(
                string.IsNullOrWhiteSpace(DockerApi.HttpUrl) ? defaults.HttpUrl : DockerApi.HttpUrl,
                configuredPipeNames.Count == 0 ? defaults.PipeNames : configuredPipeNames,
                string.IsNullOrWhiteSpace(DockerApi.PipeName) ? null : DockerApi.PipeName,
                DockerApi.RequestLoggingEnabled)
        };
    }
}

internal sealed record DockerApiConfiguration(
    string HttpUrl,
    IReadOnlyList<string> PipeNames,
    string? PipeName = null,
    bool RequestLoggingEnabled = false);