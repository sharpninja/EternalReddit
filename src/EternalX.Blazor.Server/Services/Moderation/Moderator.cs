using EternalX.Blazor.Shared.Models;

namespace EternalX.Blazor.Server.Services.Moderation;

/// <summary>
/// Classifies content. Backed by the Moderator AI in production; the default
/// implementation is a deterministic heuristic. Implementations must treat the
/// content as untrusted DATA, never as instructions to follow.
/// </summary>
public interface IModerationClassifier
{
    Task<ModerationVerdict> ClassifyAsync(string content, CancellationToken ct = default);
}

/// <summary>The verdict plus the action the system will take.</summary>
public readonly record struct ModerationOutcome(ModerationVerdict Verdict, ModerationAction Action)
{
    public bool IsAllowed => Action == ModerationAction.Allow;
}

/// <summary>Reviews a piece of content and decides what happens to it.</summary>
public interface IModerator
{
    Task<ModerationOutcome> ReviewAsync(string content, CancellationToken ct = default);
}

/// <summary>
/// Runs the deterministic injection heuristic first (fast, ban-worthy), then
/// defers to the classifier for NSFW/clean, and maps the verdict through the
/// policy. Injection short-circuits so a jailbreak never reaches the AI classifier.
/// </summary>
public sealed class Moderator : IModerator
{
    private readonly IModerationClassifier _classifier;

    public Moderator(IModerationClassifier classifier) => _classifier = classifier;

    public async Task<ModerationOutcome> ReviewAsync(string content, CancellationToken ct = default)
    {
        if (InjectionHeuristics.LooksLikeInjection(content))
            return Outcome(ModerationVerdict.PromptInjection);

        var verdict = await _classifier.ClassifyAsync(content, ct);
        return Outcome(verdict);
    }

    private static ModerationOutcome Outcome(ModerationVerdict verdict)
        => new(verdict, ModerationPolicy.Decide(verdict));
}

/// <summary>
/// Deterministic fallback classifier used until the Moderator AI is wired in.
/// Catches obvious NSFW keywords and the injection heuristic; everything else
/// is treated as clean.
/// </summary>
public sealed class HeuristicModerationClassifier : IModerationClassifier
{
    private static readonly string[] NsfwTerms =
    {
        "nsfw", "explicit", "porn", "pornographic", "hardcore", "xxx"
    };

    public Task<ModerationVerdict> ClassifyAsync(string content, CancellationToken ct = default)
    {
        if (InjectionHeuristics.LooksLikeInjection(content))
            return Task.FromResult(ModerationVerdict.PromptInjection);

        var lower = (content ?? "").ToLowerInvariant();
        foreach (var term in NsfwTerms)
            if (lower.Contains(term))
                return Task.FromResult(ModerationVerdict.Nsfw);

        return Task.FromResult(ModerationVerdict.Clean);
    }
}
