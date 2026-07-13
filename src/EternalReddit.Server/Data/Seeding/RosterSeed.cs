namespace EternalReddit.Server.Data.Seeding;

/// <summary>
/// Idempotently seeds the default peer groups, figures, and communities. Uses per-id
/// "insert if absent" so re-runs are no-ops and admin edits are never clobbered, while
/// a later release can still add new defaults. Must run before the startup purge, which
/// validates existing replies against the roster.
/// </summary>
public static class RosterSeed
{
    public static void EnsureSeeded(IPeerGroupStore groups, IFigureStore figures, ICommunityStore communities, IPostStore? posts = null)
    {
        foreach (var g in DefaultRoster.Groups)
            if (groups.Get(g.Slug) is null) groups.Upsert(g);

        foreach (var f in DefaultRoster.Figures)
            if (figures.Get(f.Name) is null) figures.Upsert(f);

        foreach (var c in DefaultRoster.Communities)
            if (communities.Get(c.Slug) is null) communities.Upsert(c);

        // First dev-blog post, once (only when a post store is supplied).
        if (posts is not null && posts.GetRecent(int.MaxValue).All(p => p.Community != "devblog"))
            posts.Add(DevBlogSeed.FirstPost());
    }
}
