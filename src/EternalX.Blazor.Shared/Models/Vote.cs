namespace EternalX.Blazor.Shared.Models;

/// <summary>A single user's vote on a post or comment. One vote per user per target.</summary>
public sealed class Vote
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = "";
    public TargetKind TargetKind { get; set; }
    public Guid TargetId { get; set; }
    public VoteKind Kind { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
