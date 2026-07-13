namespace EternalReddit.Shared.Models;

/// <summary>Admin-controlled runtime toggles for the AI feed.</summary>
public sealed class AppSettings
{
    /// <summary>When true, the hourly character auto-poster is idle.</summary>
    public bool AutoPosterPaused { get; set; }

    /// <summary>When true, the background auto-reply service is idle.</summary>
    public bool AutoRepliesPaused { get; set; }

    /// <summary>
    /// Agents the admin has benched. Their API keys stay configured; the providers are
    /// simply skipped when picking who writes. Empty = every configured agent plays.
    /// </summary>
    public List<AiProvider> DisabledProviders { get; set; } = new();
}
