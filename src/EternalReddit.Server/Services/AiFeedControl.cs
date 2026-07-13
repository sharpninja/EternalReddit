using EternalReddit.Shared.Models;

namespace EternalReddit.Server.Services;

/// <summary>
/// Pure guards for the AI feed's admin pause toggles. Kept as standalone logic (like
/// <see cref="AutoReplySelector"/>) so the pause behavior is unit-testable without
/// spinning up the background services.
/// </summary>
public static class AiFeedControl
{
    public static bool ShouldAutoPost(AppSettings settings) => !settings.AutoPosterPaused;
    public static bool ShouldAutoReply(AppSettings settings) => !settings.AutoRepliesPaused;
}
