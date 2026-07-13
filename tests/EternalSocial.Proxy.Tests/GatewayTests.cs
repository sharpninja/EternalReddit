using System.Net;
using EternalSocial.Proxy;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace EternalSocial.Proxy.Tests;

public class GatewayMapperTests
{
    [Fact]
    public void Live_routes_become_catch_all_routes_with_trailing_slash_clusters()
    {
        var (routes, clusters) = GatewayMapper.Map(new[]
        {
            new ProxyRoute { Prefix = "/r", Title = "Readit", Upstream = "http://eternalreddit:8080", Enabled = true },
            new ProxyRoute { Prefix = "/x", Title = "X", Upstream = "", Enabled = true },                    // soon
            new ProxyRoute { Prefix = "/d", Title = "D", Upstream = "http://d:8080", Enabled = false },      // disabled
        });

        var route = Assert.Single(routes);
        Assert.Equal("/r/{**catch-all}", route.Match.Path);
        Assert.Equal("r", route.ClusterId);

        var cluster = Assert.Single(clusters);
        Assert.Equal("http://eternalreddit:8080/", cluster.Destinations!["default"].Address);
    }

    [Theory]
    [InlineData("/r", true)]
    [InlineData("/blog", true)]
    [InlineData("/x-1", true)]
    [InlineData("/admin", false)]     // reserved
    [InlineData("/_gateway", false)]  // reserved
    [InlineData("/login", false)]     // reserved
    [InlineData("r", false)]          // no leading slash
    [InlineData("/two/deep", false)]  // one segment only
    [InlineData("/UPPER", false)]     // lowercase only
    public void Prefix_validation(string prefix, bool valid)
        => Assert.Equal(valid, GatewayMapper.IsValidPrefix(prefix));

    [Theory]
    [InlineData("", true)]
    [InlineData("http://eternalx:8080", true)]
    [InlineData("https://internal:5000/", true)]
    [InlineData("ftp://nope", false)]
    [InlineData("not a url", false)]
    public void Upstream_validation(string upstream, bool valid)
        => Assert.Equal(valid, GatewayMapper.IsValidUpstream(upstream));
}

public class RouteStoreTests
{
    [Fact]
    public void Seed_is_idempotent_and_rounds_trip()
    {
        var path = Path.Combine(Path.GetTempPath(), $"eternalsocial-test-{Guid.NewGuid():n}.db");
        try
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["LITEDB_PATH"] = path }).Build();
            using var db = new GatewayDb(config);
            var store = new LiteDbRouteStore(db);

            GatewaySeed.EnsureSeeded(store);
            GatewaySeed.EnsureSeeded(store);

            var all = store.GetAll();
            Assert.Equal(3, all.Count);
            Assert.Equal("http://eternalreddit:8080", store.Get("/r")!.Upstream);
            Assert.Equal("", store.Get("/x")!.Upstream);

            // Admin edits survive re-seeding.
            var x = store.Get("/x")!;
            x.Upstream = "http://eternalx:8080";
            store.Upsert(x);
            GatewaySeed.EnsureSeeded(store);
            Assert.Equal("http://eternalx:8080", store.Get("/x")!.Upstream);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}

public class GatewayEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public GatewayEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Testing");
            b.UseSetting("LITEDB_PATH", Path.Combine(Path.GetTempPath(), $"eternalsocial-waf-{Guid.NewGuid():n}.db"));
        });
    }

    private HttpClient Client() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task Landing_lists_the_seeded_networks()
    {
        var res = await Client().GetAsync("/");
        res.EnsureSuccessStatusCode();
        var html = await res.Content.ReadAsStringAsync();
        Assert.Contains("EternalReadit", html);
        Assert.Contains("EternalX", html);
        Assert.Contains("EternalDiscord", html);
    }

    [Fact]
    public async Task Health_is_anonymous()
        => (await Client().GetAsync("/_gateway/health")).EnsureSuccessStatusCode();

    [Fact]
    public async Task Route_api_requires_auth()
        => Assert.Equal(HttpStatusCode.Unauthorized, (await Client().GetAsync("/_gateway/routes")).StatusCode);

    [Fact]
    public async Task Admin_page_redirects_anonymous_to_login()
    {
        var res = await Client().GetAsync("/admin");
        Assert.Equal(HttpStatusCode.Found, res.StatusCode);
        Assert.Contains("/login", res.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Unlaunched_prefix_serves_coming_soon()
    {
        var res = await Client().GetAsync("/x/anything");
        res.EnsureSuccessStatusCode();
        Assert.Contains("hasn't launched", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Unknown_prefix_is_404()
        => Assert.Equal(HttpStatusCode.NotFound, (await Client().GetAsync("/zz/")).StatusCode);

    [Fact]
    public async Task Login_without_google_config_is_unavailable()
        => Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Client().GetAsync("/login")).StatusCode);
}
