namespace EternalX.Blazor.Shared.Models;

/// <summary>Audit record of a single moderation decision.</summary>
public sealed class ModerationLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public TargetKind TargetKind { get; set; }

    /// <summary>A short snippet of the moderated content (not the full text).</summary>
    public string ContentSnippet { get; set; } = "";

    public ModerationVerdict Verdict { get; set; }
    public ModerationAction Action { get; set; }

    public string? UserId { get; set; }
    public string Ip { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
