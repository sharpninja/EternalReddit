using System.Security.Claims;
using EternalReddit.Server.Data;
using EternalReddit.Server.Services;
using EternalReddit.Server.Services.Ai;
using EternalReddit.Shared.Models;

namespace EternalReddit.Server.Endpoints;

public static class PostEndpoints
{
    public sealed record CreatePostBody(string? Title, string Body);

    public static IEndpointRouteBuilder MapPostEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/posts");

        // --- Anonymous reads ---
        group.MapGet("", (IPostService svc, int? count, string? sort) =>
            Results.Ok(SortFeed(svc.GetRecent(200), sort).Take(count is > 0 ? count.Value : 50)));

        group.MapGet("/{id:guid}", (IPostService svc, Guid id) =>
            svc.Get(id) is { } post ? Results.Ok(post) : Results.NotFound());

        // --- Authenticated writes ---
        group.MapPost("", async (IPostService svc, HttpContext http, CreatePostBody body, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Title)) return Results.BadRequest("Title is required.");
            if (string.IsNullOrWhiteSpace(body.Body)) return Results.BadRequest("Body is required.");

            var request = new CreatePostRequest(
                body.Title,
                body.Body,
                AuthorId(http),
                DisplayName(http),
                http.Connection.RemoteIpAddress?.ToString() ?? "unknown");

            var result = await svc.CreateAsync(request, ct);
            return result.Status switch
            {
                CreatePostStatus.Created => Results.Created($"/api/posts/{result.Post!.Id}", result.Post),
                CreatePostStatus.RateLimited => Results.StatusCode(StatusCodes.Status429TooManyRequests),
                CreatePostStatus.Banned => Results.StatusCode(StatusCodes.Status403Forbidden),
                _ => Results.UnprocessableEntity(result.Reason)
            };
        }).RequireAuthorization();

        group.MapPost("/{id:guid}/vote", (IPostService svc, HttpContext http, Guid id, string dir) =>
            Vote(svc, http, id, null, dir)).RequireAuthorization();

        group.MapPost("/{id:guid}/replies/{replyId:guid}/vote", (IPostService svc, HttpContext http, Guid id, Guid replyId, string dir) =>
            Vote(svc, http, id, replyId, dir)).RequireAuthorization();

        group.MapPost("/{id:guid}/share", (IPostService svc, Guid id, Guid? replyId) =>
        {
            var count = svc.Share(id, replyId);
            if (count < 0) return Results.NotFound();
            var url = replyId is { } r ? $"/p/{id}#{r}" : $"/p/{id}";
            return Results.Ok(new { shareCount = count, url });
        }).RequireAuthorization();

        // --- Anonymous read: figure leaderboard by comment karma ---
        app.MapGet("/api/top-posters", (IPostService svc, int? count) =>
            Results.Ok(svc.GetTopPosters(count is > 0 ? count.Value : 10)));

        // --- Anonymous read: recent server activity for the /logs page ---
        app.MapGet("/api/logs", (Services.InMemoryLogSink sink, int? count) =>
            Results.Ok(sink.Recent(count is > 0 ? count.Value : 200)));

        // --- Authenticated: the current user's own posts (profile page) ---
        app.MapGet("/api/me/posts", (IPostService svc, HttpContext http) =>
            Results.Ok(svc.GetRecent(500).Where(p => p.AuthorUserId == AuthorId(http)).ToList()))
            .RequireAuthorization();

        // --- Anonymous read: public profile for any name (user or figure) ---
        app.MapGet("/api/users/{name}", (IPostService svc, IRosterService roster, string name) =>
        {
            var all = svc.GetRecent(500);
            var posts = all.Where(p => p.AuthorName == name).ToList();
            var comments = all
                .SelectMany(p => p.Replies
                    .Where(r => r.Figure == name)
                    .Select(r => new ProfileComment(p.Id, string.IsNullOrWhiteSpace(p.Title) ? "(untitled)" : p.Title, r.Body, r.Score, r.CreatedUtc)))
                .OrderByDescending(c => c.CreatedUtc).Take(50).ToList();
            return Results.Ok(new UserProfile(name, roster.Persona(name), posts.Count, comments.Count,
                posts.Sum(p => p.Score), comments.Sum(c => c.Score), posts, comments));
        });

        // --- Dev: seed a post authored by a specific approved figure ---
        app.MapPost("/api/seed", async (IPostService svc, IReplyGenerator gen, IRosterService roster, ICommunityStore communities, string figure, string? sub, CancellationToken ct) =>
        {
            if (!roster.IsApproved(figure)) return Results.BadRequest("Unknown figure.");
            if (gen.Available.Count == 0) return Results.BadRequest("No AI providers configured.");
            var community = communities.Get(string.IsNullOrWhiteSpace(sub) ? "allofhistory" : sub) ?? communities.Get("allofhistory");
            var slug = community?.Slug ?? "allofhistory";
            var provider = gen.Available[0];
            var ctx = community is null ? AiContext.Default : new AiContext(community.Name, community.Description, community.ResolveModel(provider));
            var draft = await gen.GeneratePostAsync(figure, roster.Persona(figure), provider, ctx, ct);
            var post = await svc.CreateSystemPostAsync(slug, figure, draft.Title, draft.Body, ct);
            return post is null ? Results.UnprocessableEntity("Blocked by moderation.") : Results.Ok(post);
        });

        return app;
    }

    private static IResult Vote(IPostService svc, HttpContext http, Guid id, Guid? replyId, string dir)
    {
        if (!TryParseDir(dir, out var kind)) return Results.BadRequest("dir must be 'up' or 'down'.");
        var outcome = svc.Vote(id, replyId, AuthorId(http), kind);
        return outcome is null ? Results.NotFound() : Results.Ok(outcome);
    }

    private static string AuthorId(HttpContext http)
        => http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
           ?? http.User.FindFirst("sub")?.Value
           ?? "";

    // OIDC providers put the display name under different claim types (and the
    // cookie's Identity.Name is often null when NameClaimType doesn't match), so
    // fall through the common ones before giving up on "anonymous".
    private static string DisplayName(HttpContext http)
        => Nonempty(http.User.Identity?.Name)
           ?? Nonempty(http.User.FindFirst("name")?.Value)
           ?? Nonempty(http.User.FindFirst(ClaimTypes.Name)?.Value)
           ?? Nonempty(http.User.FindFirst(ClaimTypes.GivenName)?.Value)
           ?? Nonempty(http.User.FindFirst("email")?.Value)
           ?? Nonempty(http.User.FindFirst(ClaimTypes.Email)?.Value)
           ?? "anonymous";

    private static string? Nonempty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    // Sort the feed: hot (default), rising, or new.
    private static readonly DateTime Epoch = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static IEnumerable<Post> SortFeed(IReadOnlyList<Post> posts, string? sort) => (sort ?? "hot").ToLowerInvariant() switch
    {
        "new" => posts.OrderByDescending(p => p.CreatedUtc),
        "rising" => posts.OrderByDescending(Rising).ThenByDescending(p => p.CreatedUtc),
        _ => posts.OrderByDescending(Hot).ThenByDescending(p => p.CreatedUtc),
    };

    // Reddit-style hot: log-scaled score plus a recency term.
    private static double Hot(Post p)
    {
        var s = p.Score;
        var order = Math.Log10(Math.Max(Math.Abs(s), 1));
        var sign = s > 0 ? 1 : s < 0 ? -1 : 0;
        return sign * order + (p.CreatedUtc - Epoch).TotalSeconds / 45000.0;
    }

    // Rising: engagement per unit age (gravity), favouring recent posts gaining traction.
    private static double Rising(Post p)
    {
        var ageHours = Math.Max(0, (DateTime.UtcNow - p.CreatedUtc).TotalHours);
        return (p.Score + p.Replies.Count) / Math.Pow(ageHours + 2, 1.5);
    }

    private static bool TryParseDir(string dir, out VoteKind kind)
    {
        switch ((dir ?? "").ToLowerInvariant())
        {
            case "up": kind = VoteKind.Up; return true;
            case "down": kind = VoteKind.Down; return true;
            default: kind = VoteKind.Up; return false;
        }
    }
}
