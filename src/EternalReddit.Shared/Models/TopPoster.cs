namespace EternalReddit.Shared.Models;

/// <summary>A figure's standing on the feed: total comment karma and comment count.</summary>
public sealed record TopPoster(string Figure, int Karma, int Comments);
