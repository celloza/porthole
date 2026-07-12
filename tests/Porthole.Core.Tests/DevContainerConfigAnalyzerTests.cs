using Porthole.Core.Models;
using Porthole.Core.Services;

namespace Porthole.Core.Tests;

public class DevContainerConfigAnalyzerTests
{
    [Fact]
    public void Analyze_ImageBasedConfig_ReturnsSupported()
    {
        const string json = """
        {
          "name": "demo",
          "image": "mcr.microsoft.com/devcontainers/dotnet:8.0",
          "forwardPorts": [5000],
          "postCreateCommand": "dotnet restore"
        }
        """;

        DevContainerCapabilityReport report = DevContainerConfigAnalyzer.Analyze(json);

        Assert.True(report.IsSupported);
        Assert.DoesNotContain(report.Diagnostics, d => d.Severity == DevContainerDiagnosticSeverity.Error);
    }

    [Fact]
    public void Analyze_DockerComposeConfig_ReturnsUnsupportedError()
    {
        const string json = """
        {
          "dockerComposeFile": "docker-compose.yml",
          "service": "api"
        }
        """;

        DevContainerCapabilityReport report = DevContainerConfigAnalyzer.Analyze(json);

        Assert.False(report.IsSupported);
        Assert.Contains(report.Diagnostics, d => d.Code == "DC020" && d.Severity == DevContainerDiagnosticSeverity.Error);
    }

    [Fact]
    public void Analyze_InvalidJson_ReturnsParseError()
    {
        const string json = "{ \"image\": " ;

        DevContainerCapabilityReport report = DevContainerConfigAnalyzer.Analyze(json);

        Assert.False(report.IsSupported);
        Assert.Contains(report.Diagnostics, d => d.Code == "DC002");
    }
}
