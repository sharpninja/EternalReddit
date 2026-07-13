using System.Net;
using System.Net.Http.Json;
using EternalReddit.Shared.Models;
using EternalReddit.Tests.Fakes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EternalReddit.Tests;

public class AdminEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string AdminEmail = "admin@test.local";
    private readonly WebApplicationFactory<Program> _factory;

    public AdminEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Testing");
            b.UseSetting("LITEDB_PATH", Path.Combine(Path.GetTempPath(), $"eternalreddit-adminep-{Guid.NewGuid():n}.db"));
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

    private HttpClient NonAdmin()
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add(TestAuthHandler.EmailHeader, "nobody@else.com");
        return c;
    }

    [Theory]
    [InlineData("GET", "/api/admin/stats")]
    [InlineData("GET", "/api/admin/settings")]
    [InlineData("GET", "/api/admin/users/banned")]
    [InlineData("GET", "/api/admin/moderation-log")]
    [InlineData("GET", "/api/admin/models")]
    [InlineData("GET", "/api/admin/agents")]
    public async Task Admin_routes_are_forbidden_for_non_admins(string method, string url)
    {
        var res = await NonAdmin().SendAsync(new HttpRequestMessage(new HttpMethod(method), url));
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    private sealed record AgentRow(string Provider, bool HasKey, bool Enabled, string? DefaultModel);

    [Fact]
    public async Task Agents_list_covers_real_providers_only()
    {
        var rows = await Admin().GetFromJsonAsync<List<AgentRow>>("/api/admin/agents");

        Assert.Equal(new[] { "Claude", "Grok", "HuggingFace", "OpenAI" },
            rows!.Select(r => r.Provider).OrderBy(p => p, StringComparer.Ordinal).ToArray());
        Assert.All(rows!, r => Assert.True(r.Enabled));          // nothing disabled by default
        Assert.All(rows!, r => Assert.False(r.HasKey));          // no keys in the test host
    }

    [Fact]
    public async Task Disabling_an_agent_persists_without_touching_its_key()
    {
        var admin = Admin();

        (await admin.PutAsJsonAsync("/api/admin/agents/Grok", new { enabled = false })).EnsureSuccessStatusCode();
        var rows = await admin.GetFromJsonAsync<List<AgentRow>>("/api/admin/agents");
        Assert.False(rows!.Single(r => r.Provider == "Grok").Enabled);

        // The toggle lives in settings; re-enabling round-trips clean.
        var settings = await admin.GetFromJsonAsync<AppSettings>("/api/admin/settings");
        Assert.Contains(AiProvider.Grok, settings!.DisabledProviders);

        (await admin.PutAsJsonAsync("/api/admin/agents/Grok", new { enabled = true })).EnsureSuccessStatusCode();
        rows = await admin.GetFromJsonAsync<List<AgentRow>>("/api/admin/agents");
        Assert.True(rows!.Single(r => r.Provider == "Grok").Enabled);
    }

    [Theory]
    [InlineData("Scripted")]
    [InlineData("User")]
    [InlineData("NotAProvider")]
    public async Task Toggling_a_non_agent_is_rejected(string provider)
    {
        var res = await Admin().PutAsJsonAsync($"/api/admin/agents/{provider}", new { enabled = false });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Community_crud_round_trips()
    {
        var admin = Admin();
        var sub = new Community { Slug = "testsub", Name = "TestSub", GroupIds = new() { "writers" },
            Models = new() { new AgentModel { Provider = AiProvider.Claude, ModelId = "claude-opus-4-8" } } };

        (await admin.PutAsJsonAsync("/api/admin/communities/testsub", sub)).EnsureSuccessStatusCode();

        var all = await admin.GetFromJsonAsync<List<Community>>("/api/admin/communities");
        var fetched = all!.Single(c => c.Slug == "testsub");
        Assert.Equal("TestSub", fetched.Name);
        Assert.Equal("claude-opus-4-8", fetched.ResolveModel(AiProvider.Claude));

        (await admin.DeleteAsync("/api/admin/communities/testsub")).EnsureSuccessStatusCode();
        all = await admin.GetFromJsonAsync<List<Community>>("/api/admin/communities");
        Assert.DoesNotContain(all!, c => c.Slug == "testsub");
    }

    [Fact]
    public async Task Figure_and_group_crud_round_trip()
    {
        var admin = Admin();

        var group = new PeerGroup { Slug = "testers", Name = "Testers" };
        (await admin.PutAsJsonAsync("/api/admin/peer-groups/testers", group)).EnsureSuccessStatusCode();

        var figure = new Figure { Name = "Ada Lovelace", Persona = "First programmer; visionary and precise.", GroupIds = new() { "testers" } };
        (await admin.PutAsJsonAsync($"/api/admin/figures/{Uri.EscapeDataString("Ada Lovelace")}", figure)).EnsureSuccessStatusCode();

        var figures = await admin.GetFromJsonAsync<List<Figure>>("/api/admin/figures");
        var ada = figures!.Single(f => f.Name == "Ada Lovelace");
        Assert.Contains("testers", ada.GroupIds);

        (await admin.DeleteAsync($"/api/admin/figures/{Uri.EscapeDataString("Ada Lovelace")}")).EnsureSuccessStatusCode();
        (await admin.DeleteAsync("/api/admin/peer-groups/testers")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Settings_round_trip_via_api()
    {
        var admin = Admin();
        (await admin.PutAsJsonAsync("/api/admin/settings", new AppSettings { AutoPosterPaused = true, AutoRepliesPaused = false }))
            .EnsureSuccessStatusCode();

        var settings = await admin.GetFromJsonAsync<AppSettings>("/api/admin/settings");
        Assert.True(settings!.AutoPosterPaused);
        Assert.False(settings.AutoRepliesPaused);

        (await admin.PutAsJsonAsync("/api/admin/settings", new AppSettings())).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Admin_can_delete_a_post_and_a_reply()
    {
        var admin = Admin();
        var created = await admin.PostAsJsonAsync("/api/posts", new { Title = "t", Body = "hello world" });
        created.EnsureSuccessStatusCode();
        var post = await created.Content.ReadFromJsonAsync<Post>();

        // Delete the Columbus reply, then the post itself.
        var columbus = post!.Replies.First();
        (await admin.DeleteAsync($"/api/admin/posts/{post.Id}/replies/{columbus.Id}")).EnsureSuccessStatusCode();
        (await admin.DeleteAsync($"/api/admin/posts/{post.Id}")).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/posts/{post.Id}")).StatusCode);
    }

    [Fact]
    public async Task Ban_and_unban_round_trip()
    {
        var admin = Admin();
        (await admin.PostAsJsonAsync("/api/admin/users/ban", new { UserId = "google:banme", Name = "Bad Actor", Reason = "spam" }))
            .EnsureSuccessStatusCode();

        var banned = await admin.GetFromJsonAsync<List<User>>("/api/admin/users/banned");
        Assert.Contains(banned!, u => u.Id == "google:banme");

        (await admin.PostAsJsonAsync("/api/admin/users/unban", new { UserId = "google:banme" })).EnsureSuccessStatusCode();
        banned = await admin.GetFromJsonAsync<List<User>>("/api/admin/users/banned");
        Assert.DoesNotContain(banned!, u => u.Id == "google:banme");
    }

    [Fact]
    public async Task Stats_reports_counts()
    {
        var stats = await Admin().GetFromJsonAsync<StatsShape>("/api/admin/stats");
        Assert.NotNull(stats);
        Assert.True(stats!.Figures >= 46);
        Assert.True(stats.Communities >= 8);
    }

    [Fact]
    public async Task Models_endpoint_returns_ok_for_admin()
    {
        // No AI providers configured in tests: an empty list, not an error.
        var res = await Admin().GetAsync("/api/admin/models");
        res.EnsureSuccessStatusCode();
        Assert.Equal("[]", (await res.Content.ReadAsStringAsync()).Trim());
    }

    [Fact]
    public async Task Communities_list_is_public()
    {
        var subs = await _factory.CreateClient().GetFromJsonAsync<List<Community>>("/api/communities");
        Assert.Contains(subs!, c => c.Slug == "allofhistory");
    }

    private sealed record StatsShape(int Posts, int Comments, int HumanComments, int BannedUsers, int Figures, int Communities);
}
