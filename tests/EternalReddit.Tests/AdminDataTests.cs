using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EternalReddit.Server.Data;
using EternalReddit.Shared.Models;
using EternalReddit.Tests.Fakes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EternalReddit.Tests;

public class AdminDataTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string AdminEmail = "admin@test.local";
    private readonly WebApplicationFactory<Program> _factory;

    public AdminDataTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Testing");
            b.UseSetting("LITEDB_PATH", Path.Combine(Path.GetTempPath(), $"eternalreddit-admindata-{Guid.NewGuid():n}.db"));
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

    private HttpClient Admin()
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add(TestAuthHandler.EmailHeader, AdminEmail);
        return c;
    }

    [Fact]
    public async Task Export_is_forbidden_for_non_admins()
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add(TestAuthHandler.EmailHeader, "nobody@else.com");
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/admin/export")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync("/api/admin/clear-feed", null)).StatusCode);
    }

    [Fact]
    public async Task Export_clear_and_restore_round_trip()
    {
        var admin = Admin();
        var store = _factory.Services.GetRequiredService<IPostStore>();
        store.Add(new Post { Title = "snapshot me", Body = "precious data", Community = "writers" });

        // Export: contains the post, the roster, the subs, and settings.
        var res = await admin.GetAsync("/api/admin/export");
        res.EnsureSuccessStatusCode();
        Assert.Contains("attachment", res.Content.Headers.ContentDisposition?.ToString() ?? res.Headers.ToString());
        var json = await res.Content.ReadAsStringAsync();
        using (var doc = JsonDocument.Parse(json))
        {
            var root = doc.RootElement;
            Assert.Equal(1, root.GetProperty("version").GetInt32());
            Assert.True(root.GetProperty("posts").GetArrayLength() >= 1);
            Assert.True(root.GetProperty("figures").GetArrayLength() >= 46);
            Assert.True(root.GetProperty("communities").GetArrayLength() >= 10);
            Assert.True(root.TryGetProperty("settings", out _));
        }

        // Clear feed: posts go, config stays.
        (await admin.PostAsync("/api/admin/clear-feed", null)).EnsureSuccessStatusCode();
        Assert.Empty((await admin.GetFromJsonAsync<List<Post>>("/api/posts"))!);
        Assert.True((await admin.GetFromJsonAsync<List<Community>>("/api/communities"))!.Count >= 10);

        // Sabotage config too: delete a figure, then restore the snapshot.
        (await admin.DeleteAsync($"/api/admin/figures/{Uri.EscapeDataString("Plato")}")).EnsureSuccessStatusCode();

        var restore = await admin.PostAsync("/api/admin/restore",
            new StringContent(json, Encoding.UTF8, "application/json"));
        restore.EnsureSuccessStatusCode();

        var posts = (await admin.GetFromJsonAsync<List<Post>>("/api/posts"))!;
        Assert.Contains(posts, p => p.Body == "precious data" && p.Community == "writers");
        var figures = (await admin.GetFromJsonAsync<List<Figure>>("/api/admin/figures"))!;
        Assert.Contains(figures, f => f.Name == "Plato");
    }

    [Fact]
    public async Task Restore_rejects_garbage()
    {
        var res = await Admin().PostAsync("/api/admin/restore",
            new StringContent("{\"version\":99}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
