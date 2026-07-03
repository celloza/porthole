namespace Porthole.Core.Models;

public sealed record ContainerSummary(
    string Id,
    string Name,
    string Image,
    int State,
    string StateText)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? ShortId : Name;

    public string ShortId => Id.Length <= 12 ? Id : Id[..12];

    public bool IsRunning => State == 2;

    public string MetadataLine => string.IsNullOrWhiteSpace(Image)
        ? StateText
        : $"{StateText} - {Image}";
}