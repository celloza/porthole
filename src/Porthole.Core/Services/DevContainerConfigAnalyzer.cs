using System.Text.Json;
using Porthole.Core.Models;

namespace Porthole.Core.Services;

public static class DevContainerConfigAnalyzer
{
    public static DevContainerCapabilityReport Analyze(string devContainerConfigJson)
    {
        if (string.IsNullOrWhiteSpace(devContainerConfigJson))
        {
            return new DevContainerCapabilityReport(
                IsSupported: false,
                [new DevContainerDiagnostic("DC000", DevContainerDiagnosticSeverity.Error, "devcontainer.json content is empty.")]);
        }

        try
        {
            using var document = JsonDocument.Parse(devContainerConfigJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new DevContainerCapabilityReport(
                    IsSupported: false,
                    [new DevContainerDiagnostic("DC001", DevContainerDiagnosticSeverity.Error, "devcontainer.json root must be an object.")]);
            }

            JsonElement root = document.RootElement;
            var diagnostics = new List<DevContainerDiagnostic>();

            bool hasImage = TryGetProperty(root, "image", out _);
            bool hasDockerFile = HasDockerfileDefinition(root);

            if (!hasImage && !hasDockerFile)
            {
                diagnostics.Add(new DevContainerDiagnostic(
                    "DC010",
                    DevContainerDiagnosticSeverity.Error,
                    "Either 'image' or 'dockerFile' (or 'build.dockerfile') must be defined.",
                    "$"));
            }

            if (TryGetProperty(root, "dockerComposeFile", out _))
            {
                diagnostics.Add(new DevContainerDiagnostic(
                    "DC020",
                    DevContainerDiagnosticSeverity.Error,
                    "dockerComposeFile is not supported in the initial Porthole DevContainers backend.",
                    "$.dockerComposeFile"));
            }

            if (TryGetProperty(root, "service", out _))
            {
                diagnostics.Add(new DevContainerDiagnostic(
                    "DC021",
                    DevContainerDiagnosticSeverity.Error,
                    "service is not supported without dockerComposeFile support.",
                    "$.service"));
            }

            if (TryGetProperty(root, "features", out JsonElement features)
                && features.ValueKind is JsonValueKind.Object
                && features.EnumerateObject().Any())
            {
                diagnostics.Add(new DevContainerDiagnostic(
                    "DC030",
                    DevContainerDiagnosticSeverity.Warning,
                    "features are currently ignored by the initial backend implementation.",
                    "$.features"));
            }

            if (TryGetProperty(root, "mounts", out JsonElement mounts)
                && mounts.ValueKind is JsonValueKind.Array
                && mounts.GetArrayLength() > 0)
            {
                diagnostics.Add(new DevContainerDiagnostic(
                    "DC031",
                    DevContainerDiagnosticSeverity.Warning,
                    "custom mounts are not fully supported yet; workspace mount via virtiofs is used.",
                    "$.mounts"));
            }

            if (TryGetProperty(root, "runArgs", out JsonElement runArgs)
                && runArgs.ValueKind is JsonValueKind.Array
                && runArgs.GetArrayLength() > 0)
            {
                diagnostics.Add(new DevContainerDiagnostic(
                    "DC032",
                    DevContainerDiagnosticSeverity.Warning,
                    "runArgs are not fully supported and may be ignored.",
                    "$.runArgs"));
            }

            if (TryGetProperty(root, "initializeCommand", out _))
            {
                diagnostics.Add(new DevContainerDiagnostic(
                    "DC040",
                    DevContainerDiagnosticSeverity.Warning,
                    "initializeCommand is not currently executed by the backend.",
                    "$.initializeCommand"));
            }

            bool supportsHooks = TryGetProperty(root, "postCreateCommand", out _) || TryGetProperty(root, "postStartCommand", out _);
            if (supportsHooks)
            {
                diagnostics.Add(new DevContainerDiagnostic(
                    "DC100",
                    DevContainerDiagnosticSeverity.Info,
                    "postCreateCommand/postStartCommand detected and will be considered during lifecycle orchestration."));
            }

            bool hasError = diagnostics.Any(d => d.Severity == DevContainerDiagnosticSeverity.Error);
            if (!diagnostics.Any())
            {
                diagnostics.Add(new DevContainerDiagnostic(
                    "DC101",
                    DevContainerDiagnosticSeverity.Info,
                    "No blocking diagnostics found for the initial DevContainers backend path."));
            }

            return new DevContainerCapabilityReport(!hasError, diagnostics);
        }
        catch (JsonException ex)
        {
            return new DevContainerCapabilityReport(
                IsSupported: false,
                [new DevContainerDiagnostic("DC002", DevContainerDiagnosticSeverity.Error, $"Invalid JSON: {ex.Message}")]);
        }
    }

    private static bool HasDockerfileDefinition(JsonElement root)
    {
        if (TryGetProperty(root, "dockerFile", out _))
        {
            return true;
        }

        if (TryGetProperty(root, "build", out JsonElement build)
            && build.ValueKind == JsonValueKind.Object
            && TryGetProperty(build, "dockerfile", out _))
        {
            return true;
        }

        return false;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (JsonProperty prop in element.EnumerateObject())
        {
            if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
