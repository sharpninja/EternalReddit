using EternalReddit.Server.Data.Seeding;
using EternalReddit.Server.Services;
using EternalReddit.Server.Services.Ai;
using EternalReddit.Server.Services.Moderation;
using EternalReddit.Server.Services.RateLimiting;
using EternalReddit.Shared.Models;
using EternalReddit.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EternalReddit.Tests;

public class CommunityThreadingTests
{
    private static readonly string[] Composers = { "Wolfgang Amadeus Mozart", "Johann Sebastian Bach", "Ludwig van Beethoven" };

    private readonly InMemoryPostStore _posts = new();
    private readonly InMemoryUserStore _users = new();
    private readonly InMemoryModerationLogStore _logs = new();
    private readonly InMemoryPeerGroupStore _groups = new();
    private readonly InMemoryFigureStore _figures = new();
    private readonly InMemoryCommunityStore _communities = new();

    private PostService Build(IReplyGenerator gen)
    {
        RosterSeed.EnsureSeeded(_groups, _figures, _communities);
        var limiter = new SlidingWindowRateLimiter(new FakeClock(), 100, TimeSpan.FromMinutes(1));
        var moderator = new Moderator(new StubClassifier(ModerationVerdict.Clean));
        var roster = new RosterService(_figures);
        return new PostService(_posts, _users, _logs, limiter, moderator, gen, new NullFeedNotifier(), _communities, roster, NullLogger<PostService>.Instance);
    }

    [Fact]
    public async Task Replies_in_a_themed_sub_only_use_that_groups_figures()
    {
        var svc = Build(new ReplyGenerator(new[] { new FakeAiProvider(AiProvider.Claude, "in character") }));

        var post = await svc.CreateSystemPostAsync("composers", "Johann Sebastian Bach", "Counterpoint?", "Fugues, anyone?");

        Assert.NotNull(post);
        Assert.Equal("composers", post!.Community);
        foreach (var r in post.Replies.Where(r => r.Provider != AiProvider.Scripted))
            Assert.Contains(r.Figure, Composers);
    }

    [Fact]
    public async Task Per_sub_model_override_reaches_the_provider()
    {
        var fake = new FakeAiProvider(AiProvider.Claude, "ok");
        var svc = Build(new ReplyGenerator(new[] { fake }));

        await svc.CreateSystemPostAsync("composers", "Johann Sebastian Bach", "Counterpoint?", "Fugues, anyone?");

        Assert.Equal("claude-haiku-4-5", fake.LastModel); // composers sub overrides Claude
    }

    [Fact]
    public async Task Open_sub_has_no_model_override()
    {
        var fake = new FakeAiProvider(AiProvider.Claude, "ok");
        var svc = Build(new ReplyGenerator(new[] { fake }));

        await svc.CreateSystemPostAsync("allofhistory", "Plato", "Forms?", "Discuss the Forms.");

        Assert.Null(fake.LastModel);
    }

    [Fact]
    public async Task Reply_records_the_effective_model()
    {
        var fake = new FakeAiProvider(AiProvider.Claude, "ok"); // DefaultModel = "fake-default"
        var svc = Build(new ReplyGenerator(new[] { fake }));

        // Sub with a Claude override: the override is recorded on the reply.
        var sub = await svc.CreateSystemPostAsync("composers", "Johann Sebastian Bach", "Counterpoint?", "Fugues?");
        var overridden = sub!.Replies.First(r => r.Provider == AiProvider.Claude);
        Assert.Equal("claude-haiku-4-5", overridden.Model);

        // Open sub, no override: the provider's default model is recorded.
        var open = await svc.CreateSystemPostAsync("allofhistory", "Plato", "Forms?", "Discuss.");
        var defaulted = open!.Replies.First(r => r.Provider == AiProvider.Claude);
        Assert.Equal("fake-default", defaulted.Model);

        // The scripted Columbus gag carries no model.
        Assert.Null(sub.Replies.First(r => r.Provider == AiProvider.Scripted).Model);
    }

    [Fact]
    public async Task Prompt_uses_the_community_name_not_a_hardcoded_sub()
    {
        var fake = new FakeAiProvider(AiProvider.Claude, "ok");
        var svc = Build(new ReplyGenerator(new[] { fake }));

        await svc.CreateSystemPostAsync("composers", "Johann Sebastian Bach", "Counterpoint?", "Fugues, anyone?");

        Assert.Contains("Composers", fake.LastSystem!);
        Assert.DoesNotContain("AllOfHistory", fake.LastSystem!);
    }
}
