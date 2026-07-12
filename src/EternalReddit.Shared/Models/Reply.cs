namespace EternalReddit.Shared.Models;

/// <summary>An AI-generated comment from a historical/legendary/mythical figure.</summary>
public sealed class Reply
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The figure speaking, e.g. "Isaac Newton".</summary>
    public string Figure { get; set; } = "";

    /// <summary>Which AI provider generated this comment.</summary>
    public AiProvider Provider { get; set; }

    public string Body { get; set; } = "";

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public int Upvotes { get; set; }
    public int Downvotes { get; set; }
    public int ShareCount { get; set; }

    /// <summary>Comment this one answers, for threaded crossovers. Null = top-level.</summary>
    public Guid? ParentReplyId { get; set; }

    /// <summary>True when produced by the background auto-reply service.</summary>
    public bool IsBackground { get; set; }

    public int Score => Upvotes - Downvotes;
}
