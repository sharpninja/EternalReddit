using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace EternalReddit.Server.Auth;

/// <summary>
/// Gateway mode: the EternalSocial proxy performs the Google OIDC sign-in and
/// forwards the resulting identity on every proxied request as X-Auth-* headers,
/// proven by a shared X-Gateway-Key. This handler turns those headers into the
/// request principal. Never trusted without the matching key, and the app has no
/// public port - only the proxy can reach it.
/// </summary>
public sealed class GatewayAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string Scheme = "Gateway";
    public const string KeyHeader = "X-Gateway-Key";
    public const string UserIdHeader = "X-Auth-UserId";
    public const string NameHeader = "X-Auth-Name";
    public const string EmailHeader = "X-Auth-Email";

    private readonly IConfiguration _config;

    public GatewayAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
        UrlEncoder encoder, IConfiguration config)
        : base(options, logger, encoder) => _config = config;

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var key = _config["GATEWAY_KEY"];
        if (string.IsNullOrEmpty(key) || Request.Headers[KeyHeader] != key)
            return Task.FromResult(AuthenticateResult.NoResult());

        var userId = Request.Headers[UserIdHeader].ToString();
        if (string.IsNullOrEmpty(userId))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
        var name = Request.Headers[NameHeader].ToString();
        if (name.Length > 0)
        {
            claims.Add(new Claim("name", name));
            claims.Add(new Claim(ClaimTypes.Name, name));
        }
        var email = Request.Headers[EmailHeader].ToString();
        if (email.Length > 0) claims.Add(new Claim("email", email));

        var identity = new ClaimsIdentity(claims, Scheme, ClaimTypes.Name, ClaimTypes.Role);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme)));
    }
}
