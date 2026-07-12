using System.Text.RegularExpressions;

namespace EternalX.Blazor.Server.Services.Moderation;

/// <summary>
/// Deterministic first-pass detector for the most common prompt-injection
/// patterns. Defense-in-depth in front of the Moderator AI - never the only
/// line of defense. Patterns are kept conservative to limit false positives,
/// since an injection verdict escalates to a ban.
/// </summary>
public static class InjectionHeuristics
{
    private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    private static readonly Regex[] Patterns =
    {
        new(@"\b(ignore|disregard|forget|override)\b[^.]{0,40}\b(all\s+)?(previous|prior|earlier|above|the\s+following)\b[^.]{0,20}\b(instruction|instructions|guideline|guidelines|rule|rules|prompt|prompts)\b", Opts),
        new(@"\byou\s+are\s+now\b", Opts),
        new(@"\bdeveloper\s+mode\b", Opts),
        new(@"\bsystem\s+prompt\b[^.]{0,40}\b(reveal|show|print|ignore|override|comply|leak)\b", Opts),
        new(@"\b(reveal|show|print|leak|ignore|override|comply)\b[^.]{0,40}\bsystem\s+prompt\b", Opts),
        new(@"\bact\s+as\b[^.]{0,30}\b(unfiltered|jailbroken|dan|no\s+restrictions|without\s+restrictions)\b", Opts),
    };

    public static bool LooksLikeInjection(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return false;
        foreach (var p in Patterns)
            if (p.IsMatch(content)) return true;
        return false;
    }
}
