using EternalReddit.Server.Data;
using EternalReddit.Server.Services.Ai;

namespace EternalReddit.Server.Services;

/// <summary>
/// Keeps the feed alive: if no post has appeared for an hour, a character drafts
/// and publishes an original post.
/// </summary>
public sealed class CharacterPostBackgroundService : BackgroundService
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan Quiet = TimeSpan.FromHours(1);

    private readonly IPostStore _posts;
    private readonly IPostService _service;
    private readonly IReplyGenerator _generator;
    private readonly ILogger<CharacterPostBackgroundService> _log;
    private readonly RoundRobinSelector? _rotation;

    public CharacterPostBackgroundService(
        IPostStore posts,
        IPostService service,
        IReplyGenerator generator,
        ILogger<CharacterPostBackgroundService> log)
    {
        _posts = posts;
        _service = service;
        _generator = generator;
        _log = log;
        _rotation = generator.Available.Count > 0 ? new RoundRobinSelector(generator.Available) : null;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_rotation is null)
        {
            _log.LogInformation("Character-post service idle: no AI providers configured.");
            return;
        }

        try
        {
            using var timer = new PeriodicTimer(CheckEvery);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try { await TickAsync(stoppingToken); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.LogError(ex, "Character-post tick failed");
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
        var latest = _posts.GetRecent(1).FirstOrDefault();
        if (latest is not null && DateTime.UtcNow - latest.CreatedUtc < Quiet) return;

        var figure = Figures.Pick();
        var draft = await _generator.GeneratePostAsync(figure, _rotation!.Next(), ct);
        var post = await _service.CreateSystemPostAsync(figure, draft.Title, draft.Body, ct);
        if (post is not null)
            _log.LogInformation("{Figure} started an original post after a quiet hour", figure);
    }
}
