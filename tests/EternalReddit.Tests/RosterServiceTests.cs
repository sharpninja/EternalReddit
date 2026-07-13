using EternalReddit.Server.Services.Ai;
using EternalReddit.Tests.Fakes;
using EternalReddit.Server.Data.Seeding;
using Xunit;

namespace EternalReddit.Tests;

public class RosterServiceTests
{
    private static readonly string[] Composers = { "Wolfgang Amadeus Mozart", "Johann Sebastian Bach", "Ludwig van Beethoven" };

    private static (RosterService roster, InMemoryFigureStore figures) Seeded()
    {
        var g = new InMemoryPeerGroupStore();
        var f = new InMemoryFigureStore();
        var c = new InMemoryCommunityStore();
        RosterSeed.EnsureSeeded(g, f, c);
        return (new RosterService(f), f);
    }

    [Fact]
    public void Pick_scopes_to_allowed_groups()
    {
        var (roster, _) = Seeded();
        for (var i = 0; i < 60; i++)
            Assert.Contains(roster.Pick(new[] { "composers" }), Composers);
    }

    [Fact]
    public void Pick_open_when_no_groups_specified()
    {
        var (roster, _) = Seeded();
        var names = new HashSet<string>();
        for (var i = 0; i < 300; i++) names.Add(roster.Pick());
        Assert.True(names.Count > 8, $"expected a broad draw, got {names.Count} distinct");
    }

    [Fact]
    public void Pick_avoids_exclude_when_possible()
    {
        var (roster, _) = Seeded();
        for (var i = 0; i < 60; i++)
            Assert.NotEqual("Johann Sebastian Bach", roster.Pick(new[] { "composers" }, exclude: "Johann Sebastian Bach"));
    }

    [Fact]
    public void Pick_falls_back_when_group_has_no_enabled_figures()
    {
        var (roster, _) = Seeded();
        Assert.False(string.IsNullOrEmpty(roster.Pick(new[] { "no-such-group" })));
    }

    [Fact]
    public void IsApproved_and_Persona()
    {
        var (roster, _) = Seeded();
        Assert.True(roster.IsApproved("Plato"));
        Assert.False(roster.IsApproved("Nobody McNobody"));
        Assert.Contains("Forms", roster.Persona("Plato")!);
        Assert.Null(roster.Persona("Nobody McNobody"));
    }

    [Fact]
    public void Disabled_figures_are_not_picked()
    {
        var (_, figures) = Seeded();
        foreach (var name in new[] { "Wolfgang Amadeus Mozart", "Ludwig van Beethoven" })
        {
            var fig = figures.Get(name)!;
            fig.Enabled = false;
            figures.Upsert(fig);
        }
        var roster = new RosterService(figures);
        for (var i = 0; i < 40; i++)
            Assert.Equal("Johann Sebastian Bach", roster.Pick(new[] { "composers" }));
    }
}
