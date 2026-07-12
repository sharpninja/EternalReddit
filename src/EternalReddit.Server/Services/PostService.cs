using EternalReddit.Server.Data;
using EternalReddit.Server.Services.Ai;
using EternalReddit.Server.Services.Moderation;
using EternalReddit.Server.Services.RateLimiting;
using EternalReddit.Shared.Models;

namespace EternalReddit.Server.Services;

public sealed record CreatePostRequest(string? Title, string Body, string AuthorUserId, string AuthorName, string Ip);

public enum CreatePostStatus { Created, RateLimited, Blocked, Banned }

public sealed record CreatePostResult(CreatePostStatus Status, Post? Post, string? Reason = null, TimeSpan RetryAfter = default)
{
    public static CreatePostResult Created(Post post) => new(CreatePostStatus.Created, post);
    public static CreatePostResult RateLimited(TimeSpan retryAfter) => new(CreatePostStatus.RateLimited, null, "Rate limit exceeded", retryAfter);
    public static CreatePostResult Blocked(string reason) => new(CreatePostStatus.Blocked, null, reason);
    public static CreatePostResult Banned(string reason) => new(CreatePostStatus.Banned, null, reason);
}

/// <summary>Application service for the feed: create (moderated + rate-limited), vote, share.</summary>
public interface IPostService
{
    Task<CreatePostResult> CreateAsync(CreatePostRequest request, CancellationToken ct = default);
    bool Vote(Guid postId, Guid? replyId, VoteKind kind);
    int Share(Guid postId, Guid? replyId);
    IReadOnlyList<Post> GetRecent(int count = 50);
    Post? Get(Guid id);
}

public sealed class PostService : IPostService
{
    private const int ReplyCount = 5;

    private readonly IPostStore _posts;
    private readonly IUserStore _users;
    private readonly IModerationLogStore _log;
    private readonly IRateLimiter _rateLimiter;
    private readonly IModerator _moderator;
    private readonly IReplyGenerator _generator;
    private readonly IFeedNotifier _notifier;

    public PostService(
        IPostStore posts,
        IUserStore users,
        IModerationLogStore log,
        IRateLimiter rateLimiter,
        IModerator moderator,
        IReplyGenerator generator,
        IFeedNotifier notifier)
    {
        _posts = posts;
        _users = users;
        _log = log;
        _rateLimiter = rateLimiter;
        _moderator = moderator;
        _generator = generator;
        _notifier = notifier;
    }

    public IReadOnlyList<Post> GetRecent(int count = 50) => _posts.GetRecent(count);
    public Post? Get(Guid id) => _posts.Get(id);

    public async Task<CreatePostResult> CreateAsync(CreatePostRequest request, CancellationToken ct = default)
    {
        // Already-banned users are rejected outright.
        if (_users.Get(request.AuthorUserId) is { IsBanned: true })
            return CreatePostResult.Banned("User is banned");

        // Rate limit before any business logic.
        var rl = _rateLimiter.Check(request.Ip);
        if (!rl.Allowed)
            return CreatePostResult.RateLimited(rl.RetryAfter);

        // Moderate the inbound content.
        var outcome = await _moderator.ReviewAsync(request.Body, ct);
        LogDecision(TargetKind.Post, request.Body, outcome, request.AuthorUserId, request.Ip);

        switch (outcome.Action)
        {
            case ModerationAction.Ban:
                Ban(request.AuthorUserId, request.AuthorName, request.Ip, outcome.Verdict);
                return CreatePostResult.Banned("Prompt injection detected");
            case ModerationAction.Block:
                return CreatePostResult.Blocked(outcome.Verdict.ToString());
        }

        var post = new Post
        {
            Title = request.Title,
            Body = request.Body,
            AuthorUserId = request.AuthorUserId,
            AuthorName = request.AuthorName,
            AuthorIp = request.Ip,
            CreatedUtc = DateTime.UtcNow
        };
        _posts.Add(post);

        await GenerateReplyThreadAsync(post, ct);
        await _notifier.FeedChangedAsync();
        return CreatePostResult.Created(post);
    }

    public bool Vote(Guid postId, Guid? replyId, VoteKind kind)
    {
        var post = _posts.Get(postId);
        if (post is null) return false;

        if (replyId is null)
        {
            Apply(kind, () => post.Upvotes++, () => post.Downvotes++);
        }
        else
        {
            var reply = post.Replies.FirstOrDefault(r => r.Id == replyId.Value);
            if (reply is null) return false;
            Apply(kind, () => reply.Upvotes++, () => reply.Downvotes++);
        }

        _posts.Update(post);
        return true;
    }

    public int Share(Guid postId, Guid? replyId)
    {
        var post = _posts.Get(postId);
        if (post is null) return -1;

        int count;
        if (replyId is null)
        {
            count = ++post.ShareCount;
        }
        else
        {
            var reply = post.Replies.FirstOrDefault(r => r.Id == replyId.Value);
            if (reply is null) return -1;
            count = ++reply.ShareCount;
        }

        _posts.Update(post);
        return count;
    }

    private async Task GenerateReplyThreadAsync(Post post, CancellationToken ct)
    {
        if (_generator.Available.Count == 0) return;

        var rotation = new RoundRobinSelector(_generator.Available);
        for (var i = 0; i < ReplyCount; i++)
        {
            try
            {
                var reply = await _generator.GenerateReplyAsync(post, rotation.Next(), isBackground: false, ct);

                var outcome = await _moderator.ReviewAsync(reply.Body, ct);
                LogDecision(TargetKind.Reply, reply.Body, outcome, userId: null, ip: post.AuthorIp);
                if (outcome.IsAllowed)
                    post.Replies.Add(reply);
            }
            catch (Exception ex)
            {
                // A single provider hiccup (rate limit, transient 5xx, bad JSON) must not
                // fail the user's post. Surface it to the container log and keep going.
                Console.Error.WriteLine($"[reply-gen] post {post.Id} provider error: {ex.Message}");
            }
        }

        _posts.Update(post);
    }

    private void Ban(string userId, string name, string ip, ModerationVerdict verdict)
    {
        var user = _users.Get(userId) ?? new User { Id = userId, DisplayName = name };
        user.IsBanned = true;
        user.BannedUtc = DateTime.UtcNow;
        user.BanReason = verdict.ToString();
        _users.Upsert(user);
    }

    private void LogDecision(TargetKind kind, string content, ModerationOutcome outcome, string? userId, string ip)
        => _log.Add(new ModerationLog
        {
            TargetKind = kind,
            ContentSnippet = content.Length <= 120 ? content : content[..120],
            Verdict = outcome.Verdict,
            Action = outcome.Action,
            UserId = userId,
            Ip = ip
        });

    private static void Apply(VoteKind kind, Action up, Action down)
    {
        if (kind == VoteKind.Up) up(); else down();
    }
}
