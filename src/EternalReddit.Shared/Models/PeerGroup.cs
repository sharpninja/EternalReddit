namespace EternalReddit.Shared.Models;

/// <summary>A named group of figures (e.g. "Composers"). A sub allows one or more groups.</summary>
public sealed class PeerGroup
{
    /// <summary>Stable slug id, e.g. "composers".</summary>
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
}
