using EternalReddit.Server.Auth;
using EternalReddit.Server.Data;
using EternalReddit.Server.Services;
using EternalReddit.Shared.Models;

namespace EternalReddit.Server.Endpoints;

/// <summary>
/// The admin surface: everything here requires the "Admin" policy (the owner's Google
/// account). Communities/groups/figures CRUD (including per-sub model config, embedded
/// on the community), content moderation, ban management, and AI-feed control.
/// </summary>
public static class AdminEndpoints
{
    public sealed record BanBody(string UserId, string? Name, string? Reason);

    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin").RequireAuthorization(AdminAccess.PolicyName);

        // --- Communities (subs), incl. per-provider model overrides ---
        admin.MapGet("/communities", (ICommunityStore store) => Results.Ok(store.GetAll()));
        admin.MapPut("/communities/{slug}", (ICommunityStore store, string slug, Community body) =>
        {
            if (string.IsNullOrWhiteSpace(body.Slug) || body.Slug != slug) return Results.BadRequest("Body slug must match the route.");
            if (string.IsNullOrWhiteSpace(body.Name)) return Results.BadRequest("Name is required.");
            store.Upsert(body);
            return Results.Ok(body);
        });
        admin.MapDelete("/communities/{slug}", (ICommunityStore store, string slug) =>
        {
            if (slug == "allofhistory") return Results.BadRequest("The default community cannot be deleted.");
            store.Delete(slug);
            return Results.NoContent();
        });

        // --- Peer groups ---
        admin.MapGet("/peer-groups", (IPeerGroupStore store) => Results.Ok(store.GetAll()));
        admin.MapPut("/peer-groups/{slug}", (IPeerGroupStore store, string slug, PeerGroup body) =>
        {
            if (string.IsNullOrWhiteSpace(body.Slug) || body.Slug != slug) return Results.BadRequest("Body slug must match the route.");
            if (string.IsNullOrWhiteSpace(body.Name)) return Results.BadRequest("Name is required.");
            store.Upsert(body);
            return Results.Ok(body);
        });
        admin.MapDelete("/peer-groups/{slug}", (IPeerGroupStore store, string slug) =>
        {
            store.Delete(slug);
            return Results.NoContent();
        });

        // --- Figures (roster) ---
        admin.MapGet("/figures", (IFigureStore store) => Results.Ok(store.GetAll()));
        admin.MapPut("/figures/{name}", (IFigureStore store, string name, Figure body) =>
        {
            if (string.IsNullOrWhiteSpace(body.Name) || body.Name != name) return Results.BadRequest("Body name must match the route.");
            if (string.IsNullOrWhiteSpace(body.Persona)) return Results.BadRequest("Persona is required.");
            store.Upsert(body);
            return Results.Ok(body);
        });
        admin.MapDelete("/figures/{name}", (IFigureStore store, string name) =>
        {
            store.Delete(name);
            return Results.NoContent();
        });

        // --- Moderation ---
        admin.MapDelete("/posts/{id:guid}", (IPostService svc, Guid id) =>
            svc.DeletePost(id) ? Results.NoContent() : Results.NotFound());
        admin.MapDelete("/posts/{id:guid}/replies/{replyId:guid}", (IPostService svc, Guid id, Guid replyId) =>
            svc.DeleteReply(id, replyId) ? Results.NoContent() : Results.NotFound());

        admin.MapPost("/users/ban", (IPostService svc, BanBody body) =>
            svc.SetBanned(body.UserId, body.Name ?? "", true, body.Reason) ? Results.Ok() : Results.BadRequest());
        admin.MapPost("/users/unban", (IPostService svc, BanBody body) =>
            svc.SetBanned(body.UserId, body.Name ?? "", false, null) ? Results.Ok() : Results.BadRequest());
        admin.MapGet("/users/banned", (IUserStore users) =>
            Results.Ok(users.GetAll().Where(u => u.IsBanned).ToList()));

        // --- AI-feed control ---
        admin.MapGet("/settings", (ISettingsStore store) => Results.Ok(store.Get()));
        admin.MapPut("/settings", (ISettingsStore store, AppSettings body) =>
        {
            store.Save(body);
            return Results.Ok(body);
        });

        // --- Stats + moderation log ---
        admin.MapGet("/stats", (IPostService svc, IUserStore users, IFigureStore figures, ICommunityStore communities,
                                ISettingsStore settings, Services.Ai.IReplyGenerator gen) =>
        {
            var posts = svc.GetRecent(int.MaxValue);
            var replies = posts.SelectMany(p => p.Replies).ToList();
            return Results.Ok(new
            {
                posts = posts.Count,
                comments = replies.Count,
                humanComments = replies.Count(r => r.Provider == AiProvider.User),
                aiComments = replies.Count(r => r.Provider != AiProvider.User && r.Provider != AiProvider.Scripted),
                bannedUsers = users.GetAll().Count(u => u.IsBanned),
                figures = figures.GetAll().Count,
                communities = communities.GetAll().Count,
                providers = gen.Available,
                settings = settings.Get()
            });
        });
        admin.MapGet("/moderation-log", (IModerationLogStore log, int? count) =>
            Results.Ok(log.GetRecent(count is > 0 ? count.Value : 100)));

        return app;
    }
}
