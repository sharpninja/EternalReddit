using System.Security.Claims;
using EternalX.Blazor.Server.Services;
using EternalX.Blazor.Shared.Models;

namespace EternalX.Blazor.Server.Endpoints;

public static class PostEndpoints
{
    public sealed record CreatePostBody(string? Title, string Body);

    public static IEndpointRouteBuilder MapPostEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/posts");

        // --- Anonymous reads ---
        group.MapGet("", (IPostService svc, int? count) => Results.Ok(svc.GetRecent(count is > 0 ? count.Value : 50)));

        group.MapGet("/{id:guid}", (IPostService svc, Guid id) =>
            svc.Get(id) is { } post ? Results.Ok(post) : Results.NotFound());

        // --- Authenticated writes ---
        group.MapPost("", async (IPostService svc, HttpContext http, CreatePostBody body, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Body)) return Results.BadRequest("Body is required.");

            var request = new CreatePostRequest(
                body.Title,
                body.Body,
                AuthorId(http),
                http.User.Identity?.Name ?? "anonymous",
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

        group.MapPost("/{id:guid}/vote", (IPostService svc, Guid id, string dir) => Vote(svc, id, null, dir))
            .RequireAuthorization();

        group.MapPost("/{id:guid}/replies/{replyId:guid}/vote", (IPostService svc, Guid id, Guid replyId, string dir) =>
            Vote(svc, id, replyId, dir)).RequireAuthorization();

        group.MapPost("/{id:guid}/share", (IPostService svc, Guid id, Guid? replyId) =>
        {
            var count = svc.Share(id, replyId);
            if (count < 0) return Results.NotFound();
            var url = replyId is { } r ? $"/p/{id}#{r}" : $"/p/{id}";
            return Results.Ok(new { shareCount = count, url });
        }).RequireAuthorization();

        return app;
    }

    private static IResult Vote(IPostService svc, Guid id, Guid? replyId, string dir)
    {
        if (!TryParseDir(dir, out var kind)) return Results.BadRequest("dir must be 'up' or 'down'.");
        return svc.Vote(id, replyId, kind) ? Results.Ok() : Results.NotFound();
    }

    private static string AuthorId(HttpContext http)
        => http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
           ?? http.User.FindFirst("sub")?.Value
           ?? "";

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
