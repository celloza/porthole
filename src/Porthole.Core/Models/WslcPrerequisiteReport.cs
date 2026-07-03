namespace Porthole.Core.Models;

public sealed record WslcPrerequisiteReport(IReadOnlyList<string> MissingComponents)
{
    public bool IsReady => MissingComponents.Count == 0;

    public string RecommendedCommand => "wsl --update --pre-release";

    public string Summary => IsReady
        ? "WSL Containers prerequisites look available for the first session bootstrap."
        : $"Missing: {string.Join(", ", MissingComponents)}. Run 'wsl --update --pre-release' and relaunch Porthole.";
}