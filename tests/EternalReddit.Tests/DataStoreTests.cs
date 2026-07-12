using EternalReddit.Server.Data;
using EternalReddit.Server.Data.Seeding;
using EternalReddit.Shared.Models;
using EternalReddit.Tests.Fakes;
using Xunit;

namespace EternalReddit.Tests;

public class DataStoreTests
{
    private static string TempDb() => Path.Combine(Path.GetTempPath(), $"eternalreddit-ds-{Guid.NewGuid():n}.db");

    [Fact]
    public void Figure_round_trips_with_groups()
    {
        var path = TempDb();
        try
        {
            using var ctx = new LiteDbContext(path);
            var store = new LiteDbFigureStore(ctx);
            store.Upsert(new Figure { Name = "Isaac Newton", Persona = "precise", GroupIds = new() { "scientists" } });

            var f = store.Get("Isaac Newton");
            Assert.NotNull(f);
            Assert.Equal("precise", f!.Persona);
            Assert.Contains("scientists", f.GroupIds);
            Assert.True(f.Enabled);
            Assert.Single(store.GetAll());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Community_round_trips_with_groups_and_model_overrides()
    {
        var path = TempDb();
        try
        {
            using var ctx = new LiteDbContext(path);
            var store = new LiteDbCommunityStore(ctx);
            store.Upsert(new Community
            {
                Slug = "composers",
                Name = "Composers",
                GroupIds = new() { "composers" },
                Models = new() { new AgentModel { Provider = AiProvider.Claude, ModelId = "claude-haiku-4-5" } }
            });

            var c = store.Get("composers");
            Assert.NotNull(c);
            Assert.Equal("Composers", c!.Name);
            Assert.Contains("composers", c.GroupIds);
            Assert.Equal("claude-haiku-4-5", c.ResolveModel(AiProvider.Claude));
            Assert.Null(c.ResolveModel(AiProvider.Grok));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void PeerGroup_round_trips()
    {
        var path = TempDb();
        try
        {
            using var ctx = new LiteDbContext(path);
            var store = new LiteDbPeerGroupStore(ctx);
            store.Upsert(new PeerGroup { Slug = "generals", Name = "Generals & Strategists" });
            Assert.Equal("Generals & Strategists", store.Get("generals")!.Name);
            Assert.True(store.Any());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Post_community_round_trips()
    {
        var path = TempDb();
        try
        {
            using var ctx = new LiteDbContext(path);
            var store = new LiteDbPostStore(ctx);
            store.Add(new Post { Body = "x", Community = "composers" });
            Assert.Equal("composers", store.GetRecent()[0].Community);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Settings_round_trip()
    {
        var path = TempDb();
        try
        {
            using var ctx = new LiteDbContext(path);
            var store = new LiteDbSettingsStore(ctx);
            Assert.False(store.Get().AutoPosterPaused);
            store.Save(new AppSettings { AutoPosterPaused = true, AutoRepliesPaused = true });
            Assert.True(store.Get().AutoPosterPaused);
            Assert.True(store.Get().AutoRepliesPaused);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}

public class RosterSeedTests
{
    private static (InMemoryPeerGroupStore g, InMemoryFigureStore f, InMemoryCommunityStore c) Stores()
        => (new InMemoryPeerGroupStore(), new InMemoryFigureStore(), new InMemoryCommunityStore());

    [Fact]
    public void Seeds_all_defaults()
    {
        var (g, f, c) = Stores();
        RosterSeed.EnsureSeeded(g, f, c);

        Assert.Equal(46, f.GetAll().Count);
        Assert.Equal(8, g.GetAll().Count);
        Assert.True(c.GetAll().Count >= 8);
        Assert.Empty(c.Get("allofhistory")!.GroupIds);              // open default sub
        Assert.True(f.Get("Ronald Reagan")!.GroupIds.Count >= 2);   // multi-membership
        Assert.Contains("composers", c.Get("composers")!.GroupIds); // themed sub tied to a group
    }

    [Fact]
    public void Is_idempotent()
    {
        var (g, f, c) = Stores();
        RosterSeed.EnsureSeeded(g, f, c);
        RosterSeed.EnsureSeeded(g, f, c);
        Assert.Equal(46, f.GetAll().Count);
        Assert.Equal(8, g.GetAll().Count);
    }

    [Fact]
    public void Does_not_clobber_admin_edits()
    {
        var (g, f, c) = Stores();
        RosterSeed.EnsureSeeded(g, f, c);
        var plato = f.Get("Plato")!;
        plato.Persona = "EDITED";
        f.Upsert(plato);

        RosterSeed.EnsureSeeded(g, f, c);
        Assert.Equal("EDITED", f.Get("Plato")!.Persona);
    }

    [Fact]
    public void Every_figure_has_at_least_one_group()
    {
        var (g, f, c) = Stores();
        RosterSeed.EnsureSeeded(g, f, c);
        Assert.All(f.GetAll(), fig => Assert.NotEmpty(fig.GroupIds));
    }
}
