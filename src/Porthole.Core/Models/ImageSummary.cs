namespace Porthole.Core.Models;

public sealed record ImageSummary(
    string Id,
    string Repository,
    string Tag,
    string CreatedRelative,
    string SizeLabel,
    string Reference,
    DateTimeOffset? CreatedAtUtc = null,
    long? SizeBytes = null)
{
    public string DisplayName => $"{Repository}:{Tag}";

    public string MetadataLine => $"Created {CreatedRelative}";
}