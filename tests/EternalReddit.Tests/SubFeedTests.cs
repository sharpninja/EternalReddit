using System.Net.Http.Json;
using EternalReddit.Server.Data;
using EternalReddit.Shared.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EternalReddit.Tests;

public class SubFeedTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SubFeedTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Testing");
            b.UseSetting("LITEDB_PATH", Path.Combine(Path.GetTempPath(), $"eternalreddit-subfeed-{Guid.NewGuid():n}.db"));
        });
    }

    [Fact]
    public async Task Feed_filters_by_sub()
    {
        var store = _factory.Services.GetRequiredService<IPostStore>();
        store.Add(new Post { Body = "war stories", Community = "generals" });
        store.Add(new Post { Body = "fugues", Community = "composers" });

        var client = _factory.CreateClient();

        var generals = await client.GetFromJsonAsync<List<Post>>("/api/posts?sub=generals");
        Assert.Single(generals!);
        Assert.Equal("war stories", generals![0].Body);

        var all = await client.GetFromJsonAsync<List<Post>>("/api/posts");
        Assert.Equal(2, all!.Count);
    }
}

public class UserPostCommunityTests
{
    [Fact]
    public async Task User_post_carries_its_community_and_unknown_falls_back()
    {
        var groups = new Fakes.InMemoryPeerGroupStore();
        var figures = new Fakes.InMemoryFigureStore();
        var communities = new Fakes.InMemoryCommunityStore();
        EternalReddit.Server.Data.Seeding.RosterSeed.EnsureSeeded(groups, figures, communities);
        var svc = new EternalReddit.Server.Services.PostService(
            new Fakes.InMemoryPostStore(), new Fakes.InMemoryUserStore(), new Fakes.InMemoryModerationLogStore(),
            new EternalReddit.Server.Services.RateLimiting.SlidingWindowRateLimiter(new Fakes.FakeClock(), 100, TimeSpan.FromMinutes(1)),
            new EternalReddit.Server.Services.Moderation.Moderator(new Fakes.StubClassifier(ModerationVerdict.Clean)),
            new EternalReddit.Server.Services.Ai.ReplyGenerator(Array.Empty<EternalReddit.Server.Services.Ai.IAiProvider>()),
            new EternalReddit.Server.Services.NullFeedNotifier(), communities,
            new EternalReddit.Server.Services.Ai.RosterService(figures),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<EternalReddit.Server.Services.PostService>.Instance);

        var inSub = await svc.CreateAsync(new EternalReddit.Server.Services.CreatePostRequest(null, "hi", "u1", "Ada", "1.1.1.1", "generals"));
        Assert.Equal("generals", inSub.Post!.Community);

        var unknown = await svc.CreateAsync(new EternalReddit.Server.Services.CreatePostRequest(null, "hi2", "u1", "Ada", "1.1.1.2", "no-such-sub"));
        Assert.Equal("allofhistory", unknown.Post!.Community);
    }
}
