using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using EternalReddit.Server.Auth;
using EternalReddit.Tests.Fakes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EternalReddit.Tests;

public class AdminAccessTests
{
    private static ClaimsPrincipal With(params (string type, string value)[] claims)
        => new(new ClaimsIdentity(claims.Select(c => new Claim(c.type, c.value)), "test"));

    [Fact]
    public void Admin_email_matches_case_insensitively_under_either_claim()
    {
        Assert.True(AdminAccess.IsAdmin(With(("email", "Admin@Test.Local")), "admin@test.local"));
        Assert.True(AdminAccess.IsAdmin(With((ClaimTypes.Email, "admin@test.local")), "admin@test.local"));
    }

    [Fact]
    public void Non_admin_or_missing_email_is_not_admin()
    {
        Assert.False(AdminAccess.IsAdmin(With(("email", "someone@else.com")), "admin@test.local"));
        Assert.False(AdminAccess.IsAdmin(With(("name", "No Email")), "admin@test.local"));
    }
}

public class AdminEndpointAuthTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string AdminEmail = "admin@test.local";
    private readonly WebApplicationFactory<Program> _factory;

    public AdminEndpointAuthTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Testing");
            b.UseSetting("LITEDB_PATH", Path.Combine(Path.GetTempPath(), $"eternalreddit-admin-{Guid.NewGuid():n}.db"));
            b.UseSetting("Authorization:AdminEmail", AdminEmail);
            b.ConfigureTestServices(services =>
            {
                services.AddAuthentication(o =>
                {
                    o.DefaultScheme = TestAuthHandler.Scheme;
                    o.DefaultAuthenticateScheme = TestAuthHandler.Scheme;
                    o.DefaultChallengeScheme = TestAuthHandler.Scheme;
                }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.Scheme, _ => { });
            });
        });
    }

    private HttpClient ClientAs(string? email)
    {
        var c = _factory.CreateClient();
        if (email is not null) c.DefaultRequestHeaders.Add(TestAuthHandler.EmailHeader, email);
        return c;
    }

    [Fact]
    public async Task Me_reports_isAdmin_true_for_the_admin_email()
    {
        var me = await ClientAs(AdminEmail).GetFromJsonAsync<MeShape>("/api/me");
        Assert.True(me!.IsAdmin);
        Assert.True(me.Authenticated);
    }

    [Fact]
    public async Task Me_reports_isAdmin_false_for_others()
    {
        var me = await ClientAs("nobody@else.com").GetFromJsonAsync<MeShape>("/api/me");
        Assert.False(me!.IsAdmin);
    }

    [Fact]
    public async Task Me_is_not_admin_when_anonymous()
    {
        var me = await ClientAs(null).GetFromJsonAsync<MeShape>("/api/me");
        Assert.False(me!.IsAdmin);
        Assert.False(me.Authenticated);
    }

    [Fact]
    public async Task Seed_is_forbidden_for_a_non_admin()
    {
        var res = await ClientAs("nobody@else.com").PostAsync("/api/seed?figure=Plato", null);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Seed_is_unauthorized_when_anonymous()
    {
        var res = await ClientAs(null).PostAsync("/api/seed?figure=Plato", null);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Seed_passes_the_gate_for_the_admin()
    {
        // Admin clears authorization; with no AI providers configured it then returns
        // 400 ("No AI providers configured") - the point is it is NOT 401/403.
        var res = await ClientAs(AdminEmail).PostAsync("/api/seed?figure=Plato", null);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    private sealed record MeShape(bool Authenticated, string? Name, string[] Providers, bool IsAdmin);
}
