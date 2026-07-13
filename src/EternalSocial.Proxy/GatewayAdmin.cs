using System.Security.Claims;

namespace EternalSocial.Proxy;

/// <summary>The gateway's admin gate: a single owner account, identified by Google email.</summary>
public static class GatewayAdmin
{
    public const string PolicyName = "Admin";
    public const string DefaultAdminEmail = "plbyrd@gmail.com";

    public static string ConfiguredEmail(IConfiguration config)
        => config["Authorization:AdminEmail"] is { Length: > 0 } e ? e : DefaultAdminEmail;

    public static bool IsAdmin(ClaimsPrincipal user, string adminEmail)
    {
        var email = user.FindFirst("email")?.Value ?? user.FindFirst(ClaimTypes.Email)?.Value;
        return !string.IsNullOrEmpty(email) && string.Equals(email, adminEmail, StringComparison.OrdinalIgnoreCase);
    }
}
