namespace Porthole.Core.Models;

public sealed record PodSummary(
    string Namespace,
    string Name,
    string Phase,
    string NodeName);