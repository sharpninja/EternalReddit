using EternalReddit.Server.Data;
using EternalReddit.Server.Services.Ai;
using EternalReddit.Server.Services.Moderation;

namespace EternalReddit.Server.Services;

/// <summary>
/// Every 10 seconds, picks a recently-active-but-quiet thread and adds one
/// moderated AI reply from a rotating provider, keeping the feed alive.
/// </summary>
public sealed class AutoReplyBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan QuietFor = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ActiveWithin = TimeSpan.FromMinutes(30);

    private readonly IPostStore _posts;
    private readonly IReplyGenerator _generator;
    private readonly IModerator _moderator;
    private readonly IFeedNotifier _notifier;
    private readonly ILogger<AutoReplyBackgroundService> _log;
    private readonly RoundRobinSelector? _rotation;

    public AutoReplyBackgroundService(
        IPostStore posts,
        IReplyGenerator generator,
        IModerator moderator,
        IFeedNotifier notifier,
        ILogger<AutoReplyBackgroundService> log)
    {
        _posts = posts;
        _generator = generator;
        _moderator = moderator;
        _notifier = notifier;
        _log = log;
        _rotation = generator.Available.Count > 0 ? new RoundRobinSelector(generator.Available) : null;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_rotation is null)
        {
            _log.LogInformation("Auto-reply service idle: no AI providers configured.");
            return;
        }

        try
        {
            using var timer = new PeriodicTimer(Interval);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try { await TickAsync(stoppingToken); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.LogError(ex, "Auto-reply tick failed");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // graceful shutdown
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var thread = AutoReplySelector.SelectThread(_posts.GetRecent(20), DateTime.UtcNow, QuietFor, ActiveWithin);
        if (thread is null) return;

        var reply = await _generator.GenerateReplyAsync(thread, _rotation!.Next(), isBackground: true, ct);

        // Background replies are moderated too (Open Question 7: yes).
        var outcome = await _moderator.ReviewAsync(reply.Body, ct);
        if (!outcome.IsAllowed)
        {
            _log.LogInformation("Background reply blocked: {Verdict}", outcome.Verdict);
            return;
        }

        thread.Replies.Add(reply);
        _posts.Update(thread);
        await _notifier.FeedChangedAsync();
        _log.LogInformation("Background reply added to post {PostId} via {Provider}", thread.Id, reply.Provider);
    }
}
