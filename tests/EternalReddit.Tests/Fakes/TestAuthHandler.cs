using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EternalReddit.Tests.Fakes;

/// <summary>
/// Authenticates a request from an "X-Test-Email" header so admin-policy endpoints
/// can be exercised without a real OIDC flow. No header = anonymous (NoResult).
/// </summary>
public sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string Scheme = "Test";
    public const string EmailHeader = "X-Test-Email";

    public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(EmailHeader, out var email) || string.IsNullOrEmpty(email))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "test-sub"),
            new Claim("email", email.ToString()),
            new Claim("name", "Test User")
        };
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme)), Scheme);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
