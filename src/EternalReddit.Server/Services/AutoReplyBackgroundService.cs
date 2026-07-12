using EternalReddit.Server.Data;
using EternalReddit.Server.Services.Ai;

namespace EternalReddit.Server.Services;

/// <summary>
/// Every 10 seconds, picks a recently-active-but-quiet thread and adds one
/// moderated, approved-figure reply, keeping conversations going.
/// </summary>
public sealed class AutoReplyBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan QuietFor = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ActiveWithin = TimeSpan.FromMinutes(30);

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
        var thread = AutoReplySelector.SelectThread(_posts.GetRecent(20), DateTime.UtcNow, QuietFor, ActiveWithin);
        if (thread is null) return;

        var reply = await _service.GenerateReplyInto(thread, _rotation!.Next(), ct);
        if (reply is null) return;

        reply.IsBackground = true;
        _posts.Update(thread);
        await _notifier.FeedChangedAsync();
    }
}
