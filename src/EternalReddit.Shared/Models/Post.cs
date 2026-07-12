namespace EternalReddit.Shared.Models;

/// <summary>A user-authored submission with its embedded AI comment thread (Reddit-style).</summary>
public sealed class Post
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string? Title { get; set; }
    public string Body { get; set; } = "";

    /// <summary>OIDC subject of the author ("{provider}:{sub}").</summary>
    public string AuthorUserId { get; set; } = "";
    public string AuthorName { get; set; } = "";

    /// <summary>Source IP, captured for rate limiting and moderation records.</summary>
    public string AuthorIp { get; set; } = "";

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public int Upvotes { get; set; }
    public int Downvotes { get; set; }
    public int ShareCount { get; set; }

    public List<Reply> Replies { get; set; } = new();

    /// <summary>One vote per user per target (this post or one of its replies).</summary>
    public List<Vote> Votes { get; set; } = new();

    public int Score => Upvotes - Downvotes;
}
