namespace EternalReddit.Shared.Models;

/// <summary>Admin-controlled runtime toggles for the AI feed.</summary>
public sealed class AppSettings
{
    /// <summary>When true, the hourly character auto-poster is idle.</summary>
    public bool AutoPosterPaused { get; set; }

    /// <summary>When true, the background auto-reply service is idle.</summary>
    public bool AutoRepliesPaused { get; set; }
}
