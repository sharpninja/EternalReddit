using EternalReddit.Server.Data;
using EternalReddit.Server.Services.Ai;
using EternalReddit.Server.Services.Moderation;
using EternalReddit.Server.Services.RateLimiting;
using EternalReddit.Shared.Models;
using Microsoft.Extensions.Logging;

namespace EternalReddit.Server.Services;

public sealed record CreatePostRequest(string? Title, string Body, string AuthorUserId, string AuthorName, string Ip, string Community = "");

public enum CreatePostStatus { Created, RateLimited, Blocked, Banned }

public sealed record CreatePostResult(CreatePostStatus Status, Post? Post, string? Reason = null, TimeSpan RetryAfter = default)
{
    public static CreatePostResult Created(Post post) => new(CreatePostStatus.Created, post);
    public static CreatePostResult RateLimited(TimeSpan retryAfter) => new(CreatePostStatus.RateLimited, null, "Rate limit exceeded", retryAfter);
    public static CreatePostResult Blocked(string reason) => new(CreatePostStatus.Blocked, null, reason);
    public static CreatePostResult Banned(string reason) => new(CreatePostStatus.Banned, null, reason);
}

public sealed record AddReplyRequest(Guid PostId, Guid? ParentReplyId, string Body, string AuthorUserId, string AuthorName, string Ip);

public enum AddReplyStatus { Added, RateLimited, Blocked, Banned, PostNotFound, ParentNotFound }

public sealed record AddReplyResult(AddReplyStatus Status, Reply? Reply, string? Reason = null, TimeSpan RetryAfter = default)
{
    public static AddReplyResult Added(Reply reply) => new(AddReplyStatus.Added, reply);
    public static AddReplyResult RateLimited(TimeSpan retryAfter) => new(AddReplyStatus.RateLimited, null, "Rate limit exceeded", retryAfter);
    public static AddReplyResult Blocked(string reason) => new(AddReplyStatus.Blocked, null, reason);
    public static AddReplyResult Banned(string reason) => new(AddReplyStatus.Banned, null, reason);
    public static AddReplyResult PostNotFound() => new(AddReplyStatus.PostNotFound, null, "Post not found");
    public static AddReplyResult ParentNotFound() => new(AddReplyStatus.ParentNotFound, null, "Parent comment not found");
}

/// <summary>Application service for the feed: create (moderated + rate-limited), vote, share.</summary>
public interface IPostService
{
    Task<CreatePostResult> CreateAsync(CreatePostRequest request, CancellationToken ct = default);

    /// <summary>Create an original post authored by a character into a community (no rate limit); returns null if moderation blocks it.</summary>
    Task<Post?> CreateSystemPostAsync(string communitySlug, string authorName, string? title, string body, CancellationToken ct = default);

    /// <summary>Add a logged-in user's comment (top-level or nested) to a post.</summary>
    Task<AddReplyResult> AddUserReplyAsync(AddReplyRequest request, CancellationToken ct = default);

    /// <summary>Generate and thread one approved-figure reply into a post; returns it, or null if blocked/failed.</summary>
    Task<Reply?> GenerateReplyInto(Post post, AiProvider provider, bool background = false, CancellationToken ct = default);
    VoteResult? Vote(Guid postId, Guid? replyId, string userId, VoteKind kind);
    int Share(Guid postId, Guid? replyId);
    IReadOnlyList<Post> GetRecent(int count = 50);
    Post? Get(Guid id);
    IReadOnlyList<TopPoster> GetTopPosters(int count = 10);

    /// <summary>Remove any comments whose figure isn't in the approved cast; returns how many were purged.</summary>
    int PurgeUnapproved();

    // --- Admin moderation ---
    bool DeletePost(Guid id);
    bool DeleteReply(Guid postId, Guid replyId);
    bool SetBanned(string userId, string name, bool banned, string? reason);
}

public sealed class PostService : IPostService
{
    private const int ReplyCount = 5;

    // Serializes every read-modify-write of a post document. Writers (user actions,
    // the reply thread, the background services) otherwise race on independent
    // deserialized copies and the last save wins, silently dropping replies
    // (this is what kept eating the scripted Columbus comment).
    private readonly SemaphoreSlim _postWrite = new(1, 1);

    private readonly IPostStore _posts;
    private readonly IUserStore _users;
    private readonly IModerationLogStore _log;
    private readonly IRateLimiter _rateLimiter;
    private readonly IModerator _moderator;
    private readonly IReplyGenerator _generator;
    private readonly IFeedNotifier _notifier;
    private readonly ICommunityStore _communities;
    private readonly IRosterService _roster;
    private readonly ILogger<PostService> _logger;

    public PostService(
        IPostStore posts,
        IUserStore users,
        IModerationLogStore log,
        IRateLimiter rateLimiter,
        IModerator moderator,
        IReplyGenerator generator,
        IFeedNotifier notifier,
        ICommunityStore communities,
        IRosterService roster,
        ILogger<PostService> logger)
    {
        _posts = posts;
        _users = users;
        _log = log;
        _rateLimiter = rateLimiter;
        _moderator = moderator;
        _generator = generator;
        _notifier = notifier;
        _communities = communities;
        _roster = roster;
        _logger = logger;
    }

    // Resolve a sub slug to its community, defaulting to the open "allofhistory" sub.
    private Community ResolveCommunity(string? slug)
    {
        if (!string.IsNullOrWhiteSpace(slug) && _communities.Get(slug) is { } c) return c;
        return _communities.Get("allofhistory") ?? new Community { Slug = "allofhistory", Name = "AllOfHistory" };
    }

    public IReadOnlyList<Post> GetRecent(int count = 50) => _posts.GetRecent(count);
    public Post? Get(Guid id) => _posts.Get(id);

    public int PurgeUnapproved()
    {
        var removed = 0;
        foreach (var post in _posts.GetRecent(int.MaxValue))
        {
            var before = post.Replies.Count;
            post.Replies.RemoveAll(r => r.Provider != AiProvider.Scripted && r.Provider != AiProvider.User && !_roster.IsApproved(r.Figure));
            if (post.Replies.Count == before) continue;

            // Re-root any comment whose parent was just removed, so it stays visible.
            var ids = post.Replies.Select(r => r.Id).ToHashSet();
            foreach (var r in post.Replies)
                if (r.ParentReplyId is { } pid && !ids.Contains(pid)) r.ParentReplyId = null;

            removed += before - post.Replies.Count;
            _posts.Update(post);
        }
        return removed;
    }

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

        var community = ResolveCommunity(request.Community);
        var post = new Post
        {
            Title = request.Title,
            Body = request.Body,
            AuthorUserId = request.AuthorUserId,
            AuthorName = request.AuthorName,
            AuthorIp = request.Ip,
            Community = community.Slug,
            CreatedUtc = DateTime.UtcNow
        };

        // The running gag: Christopher Columbus is always first - inserted BEFORE the
        // post is persisted, so no version of the document ever exists without him.
        post.Replies.Insert(0, new Reply
        {
            Figure = "Christopher Columbus",
            Provider = AiProvider.Scripted,
            Body = "First!",
            CreatedUtc = DateTime.UtcNow
        });
        _posts.Add(post);
        _logger.LogInformation("Post created by {Author} in {Sub}", string.IsNullOrWhiteSpace(post.AuthorName) ? "anonymous" : post.AuthorName, community.Slug);

        if (community.AiParticipation)
            await GenerateReplyThreadAsync(post, ct);
        await _notifier.FeedChangedAsync();
        return CreatePostResult.Created(_posts.Get(post.Id) ?? post);
    }

    public async Task<Post?> CreateSystemPostAsync(string communitySlug, string authorName, string? title, string body, CancellationToken ct = default)
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
            Community = ResolveCommunity(communitySlug).Slug,
            CreatedUtc = DateTime.UtcNow
        };
        post.Replies.Insert(0, new Reply
        {
            Figure = "Christopher Columbus",
            Provider = AiProvider.Scripted,
            Body = "First!",
            CreatedUtc = DateTime.UtcNow
        });
        _posts.Add(post);
        _logger.LogInformation("Character post created by {Author}", authorName);

        await GenerateReplyThreadAsync(post, ct);
        await _notifier.FeedChangedAsync();
        return _posts.Get(post.Id) ?? post;
    }

    public async Task<AddReplyResult> AddUserReplyAsync(AddReplyRequest request, CancellationToken ct = default)
    {
        // Same guard rails as a post: banned users out, rate limit, then moderate.
        if (_users.Get(request.AuthorUserId) is { IsBanned: true })
            return AddReplyResult.Banned("User is banned");

        var rl = _rateLimiter.Check(request.Ip);
        if (!rl.Allowed) return AddReplyResult.RateLimited(rl.RetryAfter);

        var outcome = await _moderator.ReviewAsync(request.Body, ct);
        LogDecision(TargetKind.Reply, request.Body, outcome, request.AuthorUserId, request.Ip);
        switch (outcome.Action)
        {
            case ModerationAction.Ban:
                Ban(request.AuthorUserId, request.AuthorName, request.Ip, outcome.Verdict);
                return AddReplyResult.Banned("Prompt injection detected");
            case ModerationAction.Block:
                return AddReplyResult.Blocked(outcome.Verdict.ToString());
        }

        var reply = new Reply
        {
            Figure = "",                        // humans keep an empty figure (out of the leaderboard)
            Provider = AiProvider.User,
            AuthorUserId = request.AuthorUserId,
            AuthorName = request.AuthorName,
            Body = request.Body,
            ParentReplyId = request.ParentReplyId,
            CreatedUtc = DateTime.UtcNow
        };

        // Validate + append against the freshest document under the write lock.
        await _postWrite.WaitAsync(ct);
        try
        {
            var post = _posts.Get(request.PostId);
            if (post is null) return AddReplyResult.PostNotFound();
            if (request.ParentReplyId is { } pid && post.Replies.All(r => r.Id != pid))
                return AddReplyResult.ParentNotFound();
            post.Replies.Add(reply);
            _posts.Update(post);
        }
        finally { _postWrite.Release(); }

        _logger.LogInformation("{User} commented on a post", request.AuthorName);
        await _notifier.FeedChangedAsync();
        return AddReplyResult.Added(reply);
    }

    public VoteResult? Vote(Guid postId, Guid? replyId, string userId, VoteKind kind)
    {
        if (string.IsNullOrWhiteSpace(userId)) return null;

        _postWrite.Wait();
        try
        {
            return VoteLocked(postId, replyId, userId, kind);
        }
        finally { _postWrite.Release(); }
    }

    private VoteResult? VoteLocked(Guid postId, Guid? replyId, string userId, VoteKind kind)
    {
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

        var result = new VoteResult(upvotes(), downvotes(), upvotes() - downvotes(), userVote);
        _ = _notifier.ScoreChangedAsync(new ScoreUpdate(postId, replyId, result.Upvotes, result.Downvotes, result.Score));
        return result;
    }

    private static string Label(VoteKind kind) => kind == VoteKind.Up ? "up" : "down";

    /// <summary>The ancestor chain from the root comment down to <paramref name="parent"/> (inclusive, oldest first).</summary>
    public static IReadOnlyList<Reply> BranchTo(IReadOnlyList<Reply> all, Reply? parent)
    {
        if (parent is null) return Array.Empty<Reply>();
        var byId = all.ToDictionary(r => r.Id);
        var chain = new List<Reply>();
        for (var cur = parent; cur is not null;
             cur = cur.ParentReplyId is { } pid && byId.TryGetValue(pid, out var p) ? p : null)
            chain.Add(cur);
        chain.Reverse();
        return chain;
    }

    public int Share(Guid postId, Guid? replyId)
    {
        _postWrite.Wait();
        try
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
        finally { _postWrite.Release(); }
    }

    private async Task GenerateReplyThreadAsync(Post post, CancellationToken ct)
    {
        if (_generator.Available.Count == 0) return;

        var rotation = new RoundRobinSelector(_generator.Available);
        for (var i = 0; i < ReplyCount; i++)
            await GenerateReplyInto(post, rotation.Next(), background: false, ct);
    }

    public async Task<Reply?> GenerateReplyInto(Post post, AiProvider provider, bool background = false, CancellationToken ct = default)
    {
        // Work from the freshest version for context: the caller's copy may be stale
        // (the background service reads its own copy from the store).
        post = _posts.Get(post.Id) ?? post;

        // Decide the parent first so the reply can address it with full branch context,
        // and pick the speaking figure from the sub's peer groups (never the model's choice).
        var community = ResolveCommunity(post.Community);
        var parent = PickParent(post.Replies);
        var figure = _roster.Pick(community.GroupIds, exclude: parent?.Figure);
        var persona = _roster.Persona(figure);
        var branch = BranchTo(post.Replies, parent);
        var ctx = new AiContext(community.Name, community.Description, community.ResolveModel(provider), community.ResolveEffort(provider));
        try
        {
            var body = await _generator.GenerateReplyBodyAsync(post, branch, figure, persona, parent?.Figure, provider, ctx, ct);

            var outcome = await _moderator.ReviewAsync(body, ct);
            LogDecision(TargetKind.Reply, body, outcome, userId: null, ip: post.AuthorIp);
            if (!outcome.IsAllowed)
            {
                _logger.LogWarning("Reply from {Provider} blocked: {Verdict}", provider, outcome.Verdict);
                return null;
            }

            var reply = new Reply
            {
                Figure = figure,
                Provider = provider,
                Model = _generator.ResolveModelId(provider, ctx.ModelId),
                Body = body,
                ParentReplyId = parent?.Id,
                IsBackground = background,
                CreatedUtc = DateTime.UtcNow
            };
            var committed = await CommitReplyAsync(post.Id, reply, ct);
            if (!committed) return null;
            _logger.LogInformation("{Figure} replied to {Parent} via {Provider}", figure, parent?.Figure ?? "the post", provider);
            return reply;
        }
        catch (Exception ex)
        {
            // A single provider hiccup must not fail the caller.
            _logger.LogError(ex, "Reply generation via {Provider} failed", provider);
            return null;
        }
    }

    /// <summary>
    /// Atomically append a reply to the freshest version of the post. The slow AI
    /// generation happens outside; only this re-fetch + save holds the write lock,
    /// so concurrent writers can never overwrite each other's replies.
    /// </summary>
    private async Task<bool> CommitReplyAsync(Guid postId, Reply reply, CancellationToken ct)
    {
        await _postWrite.WaitAsync(ct);
        try
        {
            var fresh = _posts.Get(postId);
            if (fresh is null) return false; // post was deleted mid-generation
            if (reply.ParentReplyId is { } pid && fresh.Replies.All(r => r.Id != pid))
                reply.ParentReplyId = null; // parent vanished mid-generation: re-root
            fresh.Replies.Add(reply);
            _posts.Update(fresh);
            return true;
        }
        finally { _postWrite.Release(); }
    }

    public bool DeletePost(Guid id)
    {
        var existed = _posts.Delete(id);
        if (existed)
        {
            _logger.LogInformation("Admin deleted post {Id}", id);
            _ = _notifier.FeedChangedAsync();
        }
        return existed;
    }

    public bool DeleteReply(Guid postId, Guid replyId)
    {
        _postWrite.Wait();
        try
        {
            var post = _posts.Get(postId);
            if (post is null) return false;
            var removed = post.Replies.RemoveAll(r => r.Id == replyId);
            if (removed == 0) return false;

            // Re-root children of the deleted comment so they stay visible.
            var ids = post.Replies.Select(r => r.Id).ToHashSet();
            foreach (var r in post.Replies)
                if (r.ParentReplyId is { } pid && !ids.Contains(pid)) r.ParentReplyId = null;

            _posts.Update(post);
        }
        finally { _postWrite.Release(); }

        _logger.LogInformation("Admin deleted reply {ReplyId} from post {PostId}", replyId, postId);
        _ = _notifier.FeedChangedAsync();
        return true;
    }

    public bool SetBanned(string userId, string name, bool banned, string? reason)
    {
        if (string.IsNullOrWhiteSpace(userId)) return false;
        var user = _users.Get(userId) ?? new User { Id = userId, DisplayName = name };
        user.IsBanned = banned;
        user.BannedUtc = banned ? DateTime.UtcNow : null;
        user.BanReason = banned ? reason : null;
        _users.Upsert(user);
        _logger.LogInformation("Admin {Action} user {UserId}", banned ? "banned" : "unbanned", userId);
        return true;
    }

    // ~55% of replies nest under an existing (non-scripted) comment, else top-level.
    private static Reply? PickParent(IReadOnlyList<Reply> replies)
    {
        var candidates = replies.Where(r => r.Provider != AiProvider.Scripted).ToList();
        if (candidates.Count == 0) return null;
        return Random.Shared.Next(100) < 55 ? candidates[Random.Shared.Next(candidates.Count)] : null;
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
