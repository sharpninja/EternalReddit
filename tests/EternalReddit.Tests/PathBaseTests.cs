using System.Net.Http.Json;
using EternalReddit.Server;
using EternalReddit.Shared.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace EternalReddit.Tests;

public class PathBaseAssetTests
{
    [Fact]
    public void Index_base_href_and_manifest_link_are_rewritten()
    {
        const string html = "<head>\n    <base href=\"/\" />\n    <link rel=\"manifest\" href=\"manifest.webmanifest\" />\n</head>";
        var outHtml = PathBaseAssets.RewriteIndex(html, "/r");
        Assert.Contains("<base href=\"/r/\" />", outHtml);
        Assert.Contains("href=\"app.webmanifest\"", outHtml);
        Assert.DoesNotContain("href=\"manifest.webmanifest\"", outHtml);
    }

    [Fact]
    public void Index_rewrite_is_identity_when_no_path_base()
    {
        const string html = "<base href=\"/\" /><link rel=\"manifest\" href=\"manifest.webmanifest\" />";
        var outHtml = PathBaseAssets.RewriteIndex(html, "");
        Assert.Contains("<base href=\"/\" />", outHtml);
        Assert.Contains("href=\"app.webmanifest\"", outHtml); // manifest always flows through the endpoint
    }

    [Fact]
    public void Manifest_start_url_and_scope_are_rewritten()
    {
        const string json = "{\n  \"start_url\": \"/\",\n  \"scope\": \"/\",\n  \"name\": \"x\"\n}";
        var outJson = PathBaseAssets.RewriteManifest(json, "/r");
        Assert.Contains("\"start_url\": \"/r/\"", outJson);
        Assert.Contains("\"scope\": \"/r/\"", outJson);
    }
}

public class PathBasePrefixTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PathBasePrefixTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("environment", "Testing");
            b.UseSetting("LITEDB_PATH", Path.Combine(Path.GetTempPath(), $"eternalreddit-pathbase-{Guid.NewGuid():n}.db"));
            b.UseSetting("PATH_BASE", "/r");
        });
    }

    [Fact]
    public async Task Prefixed_health_and_api_work()
    {
        var client = _factory.CreateClient();
        Assert.True((await client.GetAsync("/r/health")).IsSuccessStatusCode);

        var subs = await client.GetFromJsonAsync<List<Community>>("/r/api/communities");
        Assert.Contains(subs!, c => c.Slug == "devblog");
    }

    [Fact]
    public async Task Prefixed_feed_returns_the_seeded_blog_post()
    {
        var posts = await _factory.CreateClient().GetFromJsonAsync<List<Post>>("/r/api/posts?sub=devblog");
        Assert.Single(posts!);
    }
}
