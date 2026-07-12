using EternalX.Blazor.Shared.Models;

namespace EternalX.Blazor.Server.Services.Moderation;

/// <summary>
/// Pure mapping from a moderation verdict to the action the system takes.
/// Kept separate from the classifier so the policy is deterministic and testable.
/// Prompt injection is the only verdict that escalates to a ban.
/// </summary>
public static class ModerationPolicy
{
    public static ModerationAction Decide(ModerationVerdict verdict) => verdict switch
    {
        ModerationVerdict.Clean => ModerationAction.Allow,
        ModerationVerdict.Nsfw => ModerationAction.Block,
        ModerationVerdict.PromptInjection => ModerationAction.Ban,
        _ => ModerationAction.Block // fail closed on anything unexpected
    };
}
