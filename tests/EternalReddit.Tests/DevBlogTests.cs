using System.Net;
using System.Net.Http.Json;
using EternalReddit.Server.Services;
using EternalReddit.Server.Services.Ai;
using EternalReddit.Server.Services.Moderation;
using EternalReddit.Server.Services.RateLimiting;
using EternalReddit.Shared.Models;
using EternalReddit.Tests.Fakes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EternalReddit.Tests;

public class DevBlogServiceTests
{
    [Fact]
    public async Task Ai_disabled_sub_gets_no_ai_replies_but_keeps_columbus()
    {
        var groups = new InMemoryPeerGroupStore();
        var figures = new InMemoryFigureStore();
        var communities = new InMemoryCommunityStore();
        EternalReddit.Server.Data.Seeding.RosterSeed.EnsureSeeded(groups, figures, communities);

        var fake = new FakeAiProvider(AiProvider.Claude, "should never appear");
        var svc = new PostService(new InMemoryPostStore(), new InMemoryUserStore(), new InMemoryModerationLogStore(),
            new SlidingWindowRateLimiter(new FakeClock(), 100, TimeSpan.FromMinutes(1)),
            new Moderator(new StubClassifier(ModerationVerdict.Clean)),
            new ReplyGenerator(new[] { fake }), new NullFeedNotifier(), communities,
            new RosterService(figures), NullLogger<PostService>.Instance);

        var result = await svc.CreateAsync(new CreatePostRequest("Release notes", "We shipped things.", "u1", "Payton", "1.1.1.1", "devblog"));

        Assert.Equal(CreatePostStatus.Created, result.Status);
        Assert.Equal("devblog", result.Post!.Community);
        Assert.Single(result.Post.Replies); // Columbus only
        Assert.Equal(AiProvider.Scripted, result.Post.Replies[0].Provider);
        Assert.Null(fake.LastSystem); // the AI was never invoked
    }
}

public class DevBlogEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string AdminEmail = "admin@test.local";
    private readonly WebApplicationFactory<Program> _factory;

    public DevBlogEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Testing");
            b.UseSetting("LITEDB_PATH", Path.Combine(Path.GetTempPath(), $"eternalreddit-devblog-{Guid.NewGuid():n}.db"));
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

    private HttpClient As(string email)
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add(TestAuthHandler.EmailHeader, email);
        return c;
    }

    [Fact]
    public async Task Devblog_is_seeded_restricted_and_ai_free_with_first_post()
    {
        var subs = await _factory.CreateClient().GetFromJsonAsync<List<Community>>("/api/communities");
        var blog = subs!.FirstOrDefault(c => c.Slug == "devblog");
        Assert.NotNull(blog);
        Assert.True(blog!.PostingRestricted);
        Assert.False(blog.AiParticipation);

        var posts = await _factory.CreateClient().GetFromJsonAsync<List<Post>>("/api/posts?sub=devblog");
        Assert.Single(posts!);
        Assert.Contains("Colosseum", posts![0].Title);
    }

    [Fact]
    public async Task Restricted_sub_rejects_non_admin_posts_but_accepts_admin()
    {
        var body = new { Title = "sneaky", Body = "should not land", Community = "devblog" };

        var denied = await As("nobody@else.com").PostAsJsonAsync("/api/posts", body);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var allowed = await As(AdminEmail).PostAsJsonAsync("/api/posts",
            new { Title = "official", Body = "release notes", Community = "devblog" });
        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
    }
}
