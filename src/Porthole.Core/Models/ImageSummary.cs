namespace Porthole.Core.Models;

public sealed record ImageSummary(
    string Id,
    string Repository,
    string Tag,
    string CreatedRelative,
    string SizeLabel,
    string Reference)
{
    public string DisplayName => $"{Repository}:{Tag}";

    public string MetadataLine => $"Created {CreatedRelative}";
}