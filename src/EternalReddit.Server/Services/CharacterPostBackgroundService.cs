using EternalReddit.Server.Data;
using EternalReddit.Server.Services.Ai;
using EternalReddit.Shared.Models;

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
    private readonly ICommunityStore _communities;
    private readonly IRosterService _roster;
    private readonly ISettingsStore _settings;
    private readonly ILogger<CharacterPostBackgroundService> _log;
    private readonly RoundRobinSelector? _rotation;

    public CharacterPostBackgroundService(
        IPostStore posts,
        IPostService service,
        IReplyGenerator generator,
        ICommunityStore communities,
        IRosterService roster,
        ISettingsStore settings,
        ILogger<CharacterPostBackgroundService> log)
    {
        _posts = posts;
        _service = service;
        _generator = generator;
        _communities = communities;
        _roster = roster;
        _settings = settings;
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
        if (!AiFeedControl.ShouldAutoPost(_settings.Get())) return; // admin-paused

        var latest = _posts.GetRecent(1).FirstOrDefault();
        if (latest is not null && DateTime.UtcNow - latest.CreatedUtc < Quiet) return;

        var community = PickCommunity();
        if (community is null) return;

        var provider = _rotation!.Next();
        var figure = _roster.Pick(community.GroupIds);
        if (string.IsNullOrEmpty(figure)) return;
        var persona = _roster.Persona(figure);
        var ctx = new AiContext(community.Name, community.Description, community.ResolveModel(provider));
        var draft = await _generator.GeneratePostAsync(figure, persona, provider, ctx, ct);
        var post = await _service.CreateSystemPostAsync(community.Slug, figure, draft.Title, draft.Body, ct);
        if (post is not null)
            _log.LogInformation("{Figure} started an original post in r/{Sub} after a quiet hour", figure, community.Slug);
    }

    private Community? PickCommunity()
    {
        var enabled = _communities.GetAll().Where(c => c.Enabled).ToList();
        return enabled.Count == 0 ? null : enabled[Random.Shared.Next(enabled.Count)];
    }
}
