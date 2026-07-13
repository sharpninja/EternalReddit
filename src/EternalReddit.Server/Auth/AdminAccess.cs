using System.Security.Claims;
using Microsoft.Extensions.Configuration;

namespace EternalReddit.Server.Auth;

/// <summary>
/// The single source of truth for "is this principal the admin?" - shared by the
/// "Admin" authorization policy and the /api/me flag so the two can never drift. The
/// admin is identified by Google email, configurable via Authorization__AdminEmail.
/// </summary>
public static class AdminAccess
{
    public const string PolicyName = "Admin";
    public const string DefaultAdminEmail = "plbyrd@gmail.com";

    public static string ConfiguredEmail(IConfiguration config)
        => config["Authorization:AdminEmail"] is { Length: > 0 } e ? e : DefaultAdminEmail;

    /// <summary>The email claim can arrive as "email" or ClaimTypes.Email; check both, case-insensitively.</summary>
    public static bool IsAdmin(ClaimsPrincipal user, string adminEmail)
    {
        var email = user.FindFirst("email")?.Value ?? user.FindFirst(ClaimTypes.Email)?.Value;
        return !string.IsNullOrEmpty(email) && string.Equals(email, adminEmail, StringComparison.OrdinalIgnoreCase);
    }
}
