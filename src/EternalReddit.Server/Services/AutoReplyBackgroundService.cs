using EternalReddit.Server.Data;
using EternalReddit.Server.Services.Ai;
using EternalReddit.Shared.Models;

namespace EternalReddit.Server.Services;

/// <summary>
/// Every 10 seconds, shows the AI the last-24h threads (sorted by activity over
/// the last hour) and lets it decide which conversation to jump into with one
/// moderated, approved-figure comment.
/// </summary>
public sealed class AutoReplyBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Window = TimeSpan.FromHours(24);

    private readonly IPostStore _posts;
    private readonly IPostService _service;
    private readonly IReplyGenerator _generator;
    private readonly IFeedNotifier _notifier;
    private readonly ILogger<AutoReplyBackgroundService> _log;
    private readonly RoundRobinSelector? _rotation;

    public AutoReplyBackgroundService(
        IPostStore posts,
        IPostService service,
        IReplyGenerator generator,
        IFeedNotifier notifier,
        ILogger<AutoReplyBackgroundService> log)
    {
        _posts = posts;
        _service = service;
        _generator = generator;
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
        var now = DateTime.UtcNow;
        var since = now - Window;

        // The menu: every thread active in the last 24h, sorted by activity in the last hour.
        var candidates = _posts.GetRecent(80)
            .Where(p => Active(p, since))
            .OrderByDescending(p => ActivityLastHour(p, now))
            .ThenByDescending(LatestActivity)
            .Take(12)
            .ToList();
        if (candidates.Count == 0) return;

        var provider = _rotation!.Next();

        // Let the AI decide which conversation to join.
        var chosen = candidates[0];
        if (candidates.Count > 1)
        {
            var menu = candidates.Select((p, i) => MenuLine(p, i + 1, now)).ToList();
            var pick = await _generator.ChooseAsync(menu,
                "You are a historical figure browsing r/AllOfHistory. From these active threads (last 24h, busiest first), choose the ONE you most want to jump into with a comment.",
                provider, ct);
            chosen = candidates[Math.Clamp(pick - 1, 0, candidates.Count - 1)];
        }

        var reply = await _service.GenerateReplyInto(chosen, provider, ct);
        if (reply is null) return;

        reply.IsBackground = true;
        _posts.Update(chosen);
        await _notifier.FeedChangedAsync();
    }

    private static bool Active(Post p, DateTime since)
        => p.CreatedUtc >= since || p.Replies.Any(r => r.CreatedUtc >= since);

    private static int ActivityLastHour(Post p, DateTime now)
    {
        var since = now.AddHours(-1);
        return p.Replies.Count(r => r.CreatedUtc >= since) + (p.CreatedUtc >= since ? 1 : 0);
    }

    private static DateTime LatestActivity(Post p)
        => p.Replies.Count == 0 ? p.CreatedUtc : p.Replies.Max(r => r.CreatedUtc);

    private static string MenuLine(Post p, int n, DateTime now)
    {
        var latest = p.Replies.OrderByDescending(r => r.CreatedUtc).FirstOrDefault();
        var snip = latest is null ? p.Body : $"{latest.Figure}: {latest.Body}";
        if (snip.Length > 120) snip = snip[..120] + "…";
        return $"{n}. \"{p.Title}\" by {p.AuthorName} - {p.Replies.Count} comments, {ActivityLastHour(p, now)} in the last hour. Latest - {snip}";
    }
}
