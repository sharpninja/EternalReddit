namespace EternalReddit.Shared.Models;

/// <summary>A comment shown on a profile, with a link back to its post.</summary>
public sealed record ProfileComment(Guid PostId, string PostTitle, string Body, int Score, DateTime CreatedUtc);

/// <summary>Public profile for a name (a real user's display name or a figure): the AI persona (figures only), posts, comments, and karma.</summary>
public sealed record UserProfile(
    string Name,
    string? Persona,
    int PostCount,
    int CommentCount,
    int PostKarma,
    int CommentKarma,
    IReadOnlyList<Post> Posts,
    IReadOnlyList<ProfileComment> Comments);
