using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace EternalX.Blazor.Tests;

public class ApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("environment", "Testing"); // no dev seeding
            builder.UseSetting("LITEDB_PATH", Path.Combine(Path.GetTempPath(), $"eternalx-it-{Guid.NewGuid():n}.db"));
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
}
