namespace EternalReddit.Server.Services.Ai;

/// <summary>
/// The per-generation context handed to <see cref="IReplyGenerator"/>: the community the
/// content belongs to (its display name flavors the prompt in place of a hardcoded sub)
/// and the resolved model id for the chosen provider (null = use the provider default).
/// </summary>
public sealed record AiContext(string CommunityName, string? CommunityDescription = null, string? ModelId = null, string? Effort = null)
{
    public static readonly AiContext Default = new("AllOfHistory");
}
