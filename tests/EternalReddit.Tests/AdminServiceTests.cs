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

public class AdminServiceTests
{
    private readonly InMemoryPostStore _posts = new();
    private readonly InMemoryUserStore _users = new();
    private readonly InMemoryModerationLogStore _logs = new();

    private PostService Build()
    {
        var groups = new InMemoryPeerGroupStore();
        var figures = new InMemoryFigureStore();
        var communities = new InMemoryCommunityStore();
        RosterSeed.EnsureSeeded(groups, figures, communities);
        return new PostService(_posts, _users, _logs,
            new SlidingWindowRateLimiter(new FakeClock(), 100, TimeSpan.FromMinutes(1)),
            new Moderator(new StubClassifier(ModerationVerdict.Clean)),
            new ReplyGenerator(Array.Empty<IAiProvider>()),
            new NullFeedNotifier(), communities, new RosterService(figures), new InMemorySettingsStore(), NullLogger<PostService>.Instance);
    }

    private async Task<Post> NewPost(PostService svc)
        => (await svc.CreateAsync(new CreatePostRequest(null, "hello", "google:u1", "Ada", "1.1.1.1"))).Post!;

    [Fact]
    public async Task DeletePost_removes_the_post()
    {
        var svc = Build();
        var post = await NewPost(svc);

        Assert.True(svc.DeletePost(post.Id));
        Assert.Null(svc.Get(post.Id));
        Assert.False(svc.DeletePost(post.Id)); // second delete: gone
    }

    [Fact]
    public async Task DeleteReply_removes_and_reroots_children()
    {
        var svc = Build();
        var post = await NewPost(svc);
        var parent = new Reply { Figure = "Plato", Provider = AiProvider.Claude, Body = "parent" };
        var child = new Reply { Figure = "Socrates", Provider = AiProvider.Claude, Body = "child", ParentReplyId = parent.Id };
        post.Replies.Add(parent);
        post.Replies.Add(child);
        _posts.Update(post);

        Assert.True(svc.DeleteReply(post.Id, parent.Id));

        var fetched = svc.Get(post.Id)!;
        Assert.DoesNotContain(fetched.Replies, r => r.Id == parent.Id);
        Assert.Null(fetched.Replies.First(r => r.Id == child.Id).ParentReplyId); // re-rooted
    }

    [Fact]
    public async Task DeleteReply_of_missing_ids_returns_false()
    {
        var svc = Build();
        var post = await NewPost(svc);
        Assert.False(svc.DeleteReply(Guid.NewGuid(), Guid.NewGuid()));
        Assert.False(svc.DeleteReply(post.Id, Guid.NewGuid()));
    }

    [Fact]
    public void SetBanned_bans_and_unbans()
    {
        var svc = Build();

        Assert.True(svc.SetBanned("google:u9", "Zed", true, "spam"));
        Assert.True(_users.Get("google:u9")!.IsBanned);
        Assert.Equal("spam", _users.Get("google:u9")!.BanReason);

        Assert.True(svc.SetBanned("google:u9", "Zed", false, null));
        Assert.False(_users.Get("google:u9")!.IsBanned);
    }
}

public class AiFeedControlTests
{
    [Fact]
    public void Guards_follow_the_settings()
    {
        Assert.True(AiFeedControl.ShouldAutoPost(new AppSettings()));
        Assert.True(AiFeedControl.ShouldAutoReply(new AppSettings()));
        Assert.False(AiFeedControl.ShouldAutoPost(new AppSettings { AutoPosterPaused = true }));
        Assert.False(AiFeedControl.ShouldAutoReply(new AppSettings { AutoRepliesPaused = true }));
    }
}
