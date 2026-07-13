using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace EternalReddit.Tests;

public class ApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("environment", "Testing"); // no dev seeding
            builder.UseSetting("LITEDB_PATH", Path.Combine(Path.GetTempPath(), $"eternalreddit-it-{Guid.NewGuid():n}.db"));
        });
    }

    [Fact]
    public async Task Health_returns_200()
    {
        var res = await _factory.CreateClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Anonymous_can_read_the_empty_feed()
    {
        var res = await _factory.CreateClient().GetAsync("/api/posts");
        res.EnsureSuccessStatusCode();
        Assert.Equal("[]", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Posting_a_reply_requires_auth()
    {
        var res = await _factory.CreateClient()
            .PostAsJsonAsync($"/api/posts/{Guid.NewGuid()}/replies", new { ParentReplyId = (Guid?)null, Body = "hi" });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
