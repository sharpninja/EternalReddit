using EternalReddit.Server.Auth;
using EternalReddit.Server.Data;
using EternalReddit.Server.Services;
using EternalReddit.Server.Services.Ai;
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

    public sealed record ProviderModelsDto(AiProvider Provider, IReadOnlyList<string> Models, string DefaultModel);

    /// <summary>One AI agent row: key presence and the admin's enable toggle (never the key itself).</summary>
    public sealed record AgentDto(string Provider, bool HasKey, bool Enabled, string? DefaultModel);
    public sealed record AgentToggleBody(bool Enabled);

    /// <summary>The providers that are actual AI agents (Scripted/User are content markers).</summary>
    private static readonly AiProvider[] RealAgents =
        { AiProvider.Claude, AiProvider.OpenAI, AiProvider.Grok, AiProvider.HuggingFace };

    // The provider model lists change rarely; cache the (network) lookups briefly.
    private static (DateTime At, List<ProviderModelsDto> Payload)? _modelsCache;

    /// <summary>Versioned full snapshot of everything data-driven (export/restore).</summary>
    public sealed record ExportBundle(
        int Version,
        DateTime ExportedUtc,
        List<Post> Posts,
        List<Community> Communities,
        List<PeerGroup> PeerGroups,
        List<Figure> Figures,
        List<User> Users,
        AppSettings Settings);

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

        // --- Agents: enable/disable a provider without touching its API key ---
        admin.MapGet("/agents", (ISettingsStore store, Services.Ai.IReplyGenerator gen) =>
        {
            var settings = store.Get();
            var rows = RealAgents
                .Select(p => new AgentDto(p.ToString(), gen.Available.Contains(p),
                    !settings.DisabledProviders.Contains(p), gen.ResolveModelId(p, null)))
                .ToList();
            return Results.Ok(rows);
        });
        admin.MapPut("/agents/{provider}", (ISettingsStore store, string provider, AgentToggleBody body) =>
        {
            if (!Enum.TryParse<AiProvider>(provider, ignoreCase: true, out var kind) || !RealAgents.Contains(kind))
                return Results.BadRequest("Unknown agent. Valid agents: " + string.Join(", ", RealAgents));

            var settings = store.Get();
            settings.DisabledProviders.Remove(kind);
            if (!body.Enabled) settings.DisabledProviders.Add(kind);
            store.Save(settings);
            return Results.Ok(new AgentDto(kind.ToString(), false, body.Enabled, null));
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

        // --- Models currently available per configured provider (for the sub editor) ---
        admin.MapGet("/models", async (IEnumerable<IAiProvider> providers, CancellationToken ct) =>
        {
            if (_modelsCache is { } cached && DateTime.UtcNow - cached.At < TimeSpan.FromMinutes(5))
                return Results.Ok(cached.Payload);

            var list = new List<ProviderModelsDto>();
            foreach (var p in providers.Where(p => p.IsConfigured))
                list.Add(new ProviderModelsDto(p.Kind, await p.ListModelsAsync(ct), p.DefaultModel));

            _modelsCache = (DateTime.UtcNow, list);
            return Results.Ok(list);
        });

        // --- Data management: export / restore / clear feed ---
        admin.MapGet("/export", (IPostStore posts, ICommunityStore communities, IPeerGroupStore groups,
                                 IFigureStore figures, IUserStore users, ISettingsStore settings) =>
        {
            var bundle = new ExportBundle(
                1,
                DateTime.UtcNow,
                posts.GetRecent(int.MaxValue).ToList(),
                communities.GetAll().ToList(),
                groups.GetAll().ToList(),
                figures.GetAll().ToList(),
                users.GetAll().ToList(),
                settings.Get());
            var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(bundle, System.Text.Json.JsonSerializerOptions.Web);
            return Results.File(bytes, "application/json", $"eternalreddit-export-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
        });

        admin.MapPost("/restore", async (IPostStore posts, ICommunityStore communities, IPeerGroupStore groups,
                                         IFigureStore figures, IUserStore users, ISettingsStore settings,
                                         Services.IFeedNotifier notifier, ExportBundle bundle) =>
        {
            if (bundle.Version != 1) return Results.BadRequest("Unsupported export version.");
            if (bundle.Posts is null || bundle.Communities is null || bundle.PeerGroups is null || bundle.Figures is null)
                return Results.BadRequest("Malformed export bundle.");

            // Replace everything with the snapshot.
            posts.Clear();
            users.Clear();
            foreach (var c in communities.GetAll()) communities.Delete(c.Slug);
            foreach (var g in groups.GetAll()) groups.Delete(g.Slug);
            foreach (var f in figures.GetAll()) figures.Delete(f.Name);

            foreach (var g in bundle.PeerGroups) groups.Upsert(g);
            foreach (var f in bundle.Figures) figures.Upsert(f);
            foreach (var c in bundle.Communities) communities.Upsert(c);
            foreach (var p in bundle.Posts) posts.Add(p);
            foreach (var u in bundle.Users ?? new List<User>()) users.Upsert(u);
            settings.Save(bundle.Settings ?? new AppSettings());

            await notifier.FeedChangedAsync();
            return Results.Ok(new
            {
                posts = bundle.Posts.Count,
                communities = bundle.Communities.Count,
                peerGroups = bundle.PeerGroups.Count,
                figures = bundle.Figures.Count,
                users = bundle.Users?.Count ?? 0
            });
        });

        admin.MapPost("/clear-feed", async (IPostStore posts, Services.IFeedNotifier notifier) =>
        {
            posts.Clear();
            await notifier.FeedChangedAsync();
            return Results.Ok();
        });

        return app;
    }
}
