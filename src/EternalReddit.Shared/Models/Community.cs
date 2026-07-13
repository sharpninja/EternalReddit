namespace EternalReddit.Shared.Models;

/// <summary>
/// A community ("sub"): a named board that scopes a feed and, via its peer groups,
/// which figures post there. An empty <see cref="GroupIds"/> means the sub is open
/// to every figure (the "independent" mode).
/// </summary>
public sealed class Community
{
    /// <summary>Stable slug id, e.g. "composers"; stored on <see cref="Post.Community"/>.</summary>
    public string Slug { get; set; } = "";

    /// <summary>Display name, shown as r/{Name} and used in AI prompts.</summary>
    public string Name { get; set; } = "";

    public string? Description { get; set; }

    /// <summary>Allowed peer groups. Empty = every figure may post here.</summary>
    public List<string> GroupIds { get; set; } = new();

    /// <summary>Per-provider model overrides for AI generated into this sub.</summary>
    public List<AgentModel> Models { get; set; } = new();

    public bool Enabled { get; set; } = true;

    /// <summary>The model id configured for a provider in this sub, or null to use the provider default.</summary>
    public string? ResolveModel(AiProvider provider)
        => Models.FirstOrDefault(m => m.Provider == provider)?.ModelId is { Length: > 0 } id ? id : null;

    /// <summary>The reasoning effort configured for a provider in this sub, or null for the provider default.</summary>
    public string? ResolveEffort(AiProvider provider)
        => Models.FirstOrDefault(m => m.Provider == provider)?.Effort is { Length: > 0 } e ? e : null;
}

/// <summary>A per-sub override of the model (and optional reasoning effort) a given AI provider uses.</summary>
public sealed class AgentModel
{
    public AiProvider Provider { get; set; }
    public string ModelId { get; set; } = "";

    /// <summary>Reasoning effort: "low" | "medium" | "high"; empty = provider default.</summary>
    public string? Effort { get; set; }
}
