namespace EternalReddit.Shared.Models;

/// <summary>Minimal profile for an authenticated user. No passwords - OIDC only.</summary>
public sealed class User
{
    /// <summary>Stable internal id: "{Provider}:{SubjectId}".</summary>
    public string Id { get; set; } = "";

    /// <summary>OIDC provider name: "google", "microsoft", or "github".</summary>
    public string Provider { get; set; } = "";

    /// <summary>The provider's subject ("sub") claim.</summary>
    public string SubjectId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public bool IsBanned { get; set; }
    public DateTime? BannedUtc { get; set; }
    public string? BanReason { get; set; }

    public static string MakeId(string provider, string subjectId) => $"{provider}:{subjectId}";
}
