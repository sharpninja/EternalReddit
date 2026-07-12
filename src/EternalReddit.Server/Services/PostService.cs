using EternalReddit.Server.Data;
using EternalReddit.Server.Services.Ai;
using EternalReddit.Server.Services.Moderation;
using EternalReddit.Server.Services.RateLimiting;
using EternalReddit.Shared.Models;
using Microsoft.Extensions.Logging;

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

    /// <summary>Create an original post authored by a character (no rate limit); returns null if moderation blocks it.</summary>
    Task<Post?> CreateSystemPostAsync(string authorName, string? title, string body, CancellationToken ct = default);
    VoteResult? Vote(Guid postId, Guid? replyId, string userId, VoteKind kind);
    int Share(Guid postId, Guid? replyId);
    IReadOnlyList<Post> GetRecent(int count = 50);
    Post? Get(Guid id);
    IReadOnlyList<TopPoster> GetTopPosters(int count = 10);
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
    private readonly ILogger<PostService> _logger;

    public PostService(
        IPostStore posts,
        IUserStore users,
        IModerationLogStore log,
        IRateLimiter rateLimiter,
        IModerator moderator,
        IReplyGenerator generator,
        IFeedNotifier notifier,
        ILogger<PostService> logger)
    {
        _posts = posts;
        _users = users;
        _log = log;
        _rateLimiter = rateLimiter;
        _moderator = moderator;
        _generator = generator;
        _notifier = notifier;
        _logger = logger;
    }

    public IReadOnlyList<Post> GetRecent(int count = 50) => _posts.GetRecent(count);
    public Post? Get(Guid id) => _posts.Get(id);

    public IReadOnlyList<TopPoster> GetTopPosters(int count = 10)
        => _posts.GetRecent(int.MaxValue)
            .SelectMany(p => p.Replies)
            .Where(r => !string.IsNullOrWhiteSpace(r.Figure))
            .GroupBy(r => r.Figure)
            .Select(g => new TopPoster(g.Key, g.Sum(r => r.Score), g.Count()))
            .OrderByDescending(t => t.Karma)
            .ThenByDescending(t => t.Comments)
            .Take(count)
            .ToList();

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
        _logger.LogInformation("Post created by {Author}", string.IsNullOrWhiteSpace(post.AuthorName) ? "anonymous" : post.AuthorName);

        // The running gag: Christopher Columbus is always first.
        post.Replies.Insert(0, new Reply
        {
            Figure = "Christopher Columbus",
            Provider = AiProvider.Scripted,
            Body = "First!",
            CreatedUtc = DateTime.UtcNow
        });

        await GenerateReplyThreadAsync(post, ct);
        _posts.Update(post); // persist Columbus even when no AI providers are configured
        await _notifier.FeedChangedAsync();
        return CreatePostResult.Created(post);
    }

    public async Task<Post?> CreateSystemPostAsync(string authorName, string? title, string body, CancellationToken ct = default)
    {
        var outcome = await _moderator.ReviewAsync(body, ct);
        if (!outcome.IsAllowed)
        {
            _logger.LogWarning("Character post from {Author} blocked: {Verdict}", authorName, outcome.Verdict);
            return null;
        }

        var post = new Post
        {
            Title = string.IsNullOrWhiteSpace(title) ? null : title,
            Body = body,
            AuthorName = authorName,
            AuthorIp = "system",
            CreatedUtc = DateTime.UtcNow
        };
        _posts.Add(post);
        _logger.LogInformation("Character post created by {Author}", authorName);

        post.Replies.Insert(0, new Reply
        {
            Figure = "Christopher Columbus",
            Provider = AiProvider.Scripted,
            Body = "First!",
            CreatedUtc = DateTime.UtcNow
        });

        await GenerateReplyThreadAsync(post, ct);
        _posts.Update(post);
        await _notifier.FeedChangedAsync();
        return post;
    }

    public VoteResult? Vote(Guid postId, Guid? replyId, string userId, VoteKind kind)
    {
        if (string.IsNullOrWhiteSpace(userId)) return null;

        var post = _posts.Get(postId);
        if (post is null) return null;

        // Resolve the target (the post itself or one of its replies) into a common
        // shape so the tally logic doesn't branch on post-vs-reply.
        Guid targetId;
        TargetKind targetKind;
        Func<int> upvotes, downvotes;
        Action<int> addUp, addDown;

        if (replyId is null)
        {
            targetId = post.Id;
            targetKind = TargetKind.Post;
            upvotes = () => post.Upvotes; downvotes = () => post.Downvotes;
            addUp = d => post.Upvotes += d; addDown = d => post.Downvotes += d;
        }
        else
        {
            var reply = post.Replies.FirstOrDefault(r => r.Id == replyId.Value);
            if (reply is null) return null;
            targetId = reply.Id;
            targetKind = TargetKind.Reply;
            upvotes = () => reply.Upvotes; downvotes = () => reply.Downvotes;
            addUp = d => reply.Upvotes += d; addDown = d => reply.Downvotes += d;
        }

        var existing = post.Votes.FirstOrDefault(v => v.UserId == userId && v.TargetId == targetId);
        string? userVote;

        if (existing is null)
        {
            // First vote from this user on this target.
            if (kind == VoteKind.Up) addUp(1); else addDown(1);
            post.Votes.Add(new Vote { UserId = userId, TargetId = targetId, TargetKind = targetKind, Kind = kind });
            userVote = Label(kind);
        }
        else if (existing.Kind == kind)
        {
            // Same arrow again: toggle the vote off.
            if (kind == VoteKind.Up) addUp(-1); else addDown(-1);
            post.Votes.Remove(existing);
            userVote = null;
        }
        else
        {
            // Switch from up to down (or vice-versa): one moves off, the other on.
            if (existing.Kind == VoteKind.Up) { addUp(-1); addDown(1); } else { addDown(-1); addUp(1); }
            existing.Kind = kind;
            userVote = Label(kind);
        }

        _posts.Update(post);
        return new VoteResult(upvotes(), downvotes(), upvotes() - downvotes(), userVote);
    }

    private static string Label(VoteKind kind) => kind == VoteKind.Up ? "up" : "down";

    /// <summary>
    /// With <paramref name="percentChance"/>%, nest this reply under an existing
    /// non-scripted comment so the thread reads like Reddit (figure-to-figure
    /// crossovers); otherwise it stays top-level.
    /// </summary>
    public static void ThreadUnder(IReadOnlyList<Reply> existing, Reply reply, int percentChance)
    {
        var parents = existing.Where(r => r.Provider != AiProvider.Scripted).ToList();
        if (parents.Count > 0 && Random.Shared.Next(100) < percentChance)
            reply.ParentReplyId = parents[Random.Shared.Next(parents.Count)].Id;
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
            var provider = rotation.Next();
            try
            {
                var reply = await _generator.GenerateReplyAsync(post, provider, isBackground: false, ct);

                var outcome = await _moderator.ReviewAsync(reply.Body, ct);
                LogDecision(TargetKind.Reply, reply.Body, outcome, userId: null, ip: post.AuthorIp);
                if (outcome.IsAllowed)
                {
                    ThreadUnder(post.Replies, reply, 55);
                    post.Replies.Add(reply);
                    _logger.LogInformation("{Figure} commented via {Provider}", reply.Figure, provider);
                }
                else
                {
                    _logger.LogWarning("Reply from {Provider} blocked: {Verdict}", provider, outcome.Verdict);
                }
            }
            catch (Exception ex)
            {
                // A single provider hiccup (rate limit, transient 5xx, bad JSON) must not
                // fail the user's post.
                _logger.LogError(ex, "Reply generation via {Provider} failed", provider);
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

}
