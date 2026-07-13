using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace EternalReddit.Tests;

/// <summary>
/// Gateway mode: with GATEWAY_KEY set, the app authenticates purely from the
/// EternalSocial proxy's forwarded identity headers (and never trusts them
/// without the matching key).
/// </summary>
public class GatewayAuthTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Key = "test-gateway-key";
    private const string AdminEmail = "admin@test.local";
    private readonly WebApplicationFactory<Program> _factory;

    public GatewayAuthTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Testing");
            b.UseSetting("LITEDB_PATH", Path.Combine(Path.GetTempPath(), $"eternalreddit-gw-{Guid.NewGuid():n}.db"));
            b.UseSetting("GATEWAY_KEY", Key);
            b.UseSetting("Authorization:AdminEmail", AdminEmail);
        });
    }

    private HttpClient Client(string? key = null, string? userId = null, string? name = null, string? email = null)
    {
        var c = _factory.CreateClient();
        if (key is not null) c.DefaultRequestHeaders.Add("X-Gateway-Key", key);
        if (userId is not null) c.DefaultRequestHeaders.Add("X-Auth-UserId", userId);
        if (name is not null) c.DefaultRequestHeaders.Add("X-Auth-Name", name);
        if (email is not null) c.DefaultRequestHeaders.Add("X-Auth-Email", email);
        return c;
    }

    private sealed record MeShape(bool Authenticated, string? Name, string[] Providers, bool IsAdmin, bool Gateway);

    [Fact]
    public async Task Anonymous_me_reports_gateway_mode_with_google_login()
    {
        var me = await Client().GetFromJsonAsync<MeShape>("/api/me");
        Assert.False(me!.Authenticated);
        Assert.True(me.Gateway);
        Assert.Contains("Google", me.Providers);
    }

    [Fact]
    public async Task Valid_gateway_headers_authenticate_the_user()
    {
        var me = await Client(Key, "google:u42", "Payton", "someone@example.com").GetFromJsonAsync<MeShape>("/api/me");
        Assert.True(me!.Authenticated);
        Assert.Equal("Payton", me.Name);
        Assert.False(me.IsAdmin);
    }

    [Fact]
    public async Task Admin_email_from_the_gateway_is_admin()
    {
        var me = await Client(Key, "google:owner", "Payton", AdminEmail).GetFromJsonAsync<MeShape>("/api/me");
        Assert.True(me!.IsAdmin);

        var stats = await Client(Key, "google:owner", "Payton", AdminEmail).GetAsync("/api/admin/stats");
        stats.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Wrong_or_missing_key_means_anonymous()
    {
        var me = await Client("wrong-key", "google:u42", "Mallory", AdminEmail).GetFromJsonAsync<MeShape>("/api/me");
        Assert.False(me!.Authenticated);

        var res = await Client(null, "google:u42", "Mallory", AdminEmail).GetAsync("/api/admin/stats");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Gateway_identity_can_write()
    {
        var res = await Client(Key, "google:u42", "Payton", "someone@example.com")
            .PostAsJsonAsync("/api/posts", new { Title = "via gateway", Body = "hello from the proxy" });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var post = await res.Content.ReadFromJsonAsync<EternalReddit.Shared.Models.Post>();
        Assert.Equal("Payton", post!.AuthorName);
    }
}
