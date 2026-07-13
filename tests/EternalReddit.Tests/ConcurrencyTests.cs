using EternalReddit.Server.Data;
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

/// <summary>
/// Guards against the lost-update race: the background auto-reply service works on
/// its own deserialized copy of a post, and its save must never wipe replies that
/// were persisted after its copy was read (this ate the Columbus "First!" comments).
/// Uses the real LiteDB store so copies are genuinely independent documents.
/// </summary>
public class ConcurrencyTests
{
    private static (PostService svc, LiteDbPostStore store, LiteDbContext ctx, string path) Build(IReplyGenerator gen)
    {
        var path = Path.Combine(Path.GetTempPath(), $"eternalreddit-race-{Guid.NewGuid():n}.db");
        var ctx = new LiteDbContext(path);
        var store = new LiteDbPostStore(ctx);
        var groups = new InMemoryPeerGroupStore();
        var figures = new InMemoryFigureStore();
        var communities = new InMemoryCommunityStore();
        RosterSeed.EnsureSeeded(groups, figures, communities);
        var svc = new PostService(store, new InMemoryUserStore(), new InMemoryModerationLogStore(),
            new SlidingWindowRateLimiter(new FakeClock(), 100, TimeSpan.FromMinutes(1)),
            new Moderator(new StubClassifier(ModerationVerdict.Clean)),
            gen, new NullFeedNotifier(), communities, new RosterService(figures), NullLogger<PostService>.Instance);
        return (svc, store, ctx, path);
    }

    [Fact]
    public async Task Reply_into_a_stale_copy_does_not_clobber_existing_replies()
    {
        var (svc, store, ctx, path) = Build(new ReplyGenerator(new[] { new FakeAiProvider(AiProvider.Claude, "background says hi") }));
        try
        {
            // Create a post with no AI thread yet: Columbus is persisted as the only reply.
            var created = (await svc.CreateAsync(new CreatePostRequest(null, "race me", "u1", "Ada", "1.1.1.1"))).Post!;
            Assert.Contains(store.Get(created.Id)!.Replies, r => r.Provider == AiProvider.Scripted);

            // The background service reads its own stale copy of the post...
            var staleCopy = store.Get(created.Id)!;

            // ...and generates a reply into it. This must commit atomically against the
            // freshest document, not overwrite it with the stale copy.
            var reply = await svc.GenerateReplyInto(staleCopy, AiProvider.Claude, background: true);

            Assert.NotNull(reply);
            var final = store.Get(created.Id)!;
            Assert.Contains(final.Replies, r => r.Provider == AiProvider.Scripted && r.Figure == "Christopher Columbus"); // survived
            Assert.Contains(final.Replies, r => r.Id == reply!.Id && r.IsBackground);                                     // committed
        }
        finally
        {
            ctx.Dispose();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Columbus_is_persisted_atomically_with_the_post()
    {
        var (svc, store, ctx, path) = Build(new ReplyGenerator(Array.Empty<IAiProvider>()));
        try
        {
            var created = (await svc.CreateAsync(new CreatePostRequest(null, "first!", "u1", "Ada", "1.1.1.1"))).Post!;

            // Straight from the DB: the very first persisted version already carries Columbus.
            var persisted = store.Get(created.Id)!;
            Assert.Equal("Christopher Columbus", persisted.Replies[0].Figure);
            Assert.Equal(AiProvider.Scripted, persisted.Replies[0].Provider);
        }
        finally
        {
            ctx.Dispose();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
