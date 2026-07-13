using EternalReddit.Server.Services;
using EternalReddit.Server.Services.Ai;
using EternalReddit.Shared.Models;
using Xunit;

namespace EternalReddit.Tests;

public class AgentControlTests
{
    private static readonly AiProvider[] All = { AiProvider.Claude, AiProvider.Grok, AiProvider.OpenAI };

    [Fact]
    public void All_agents_are_enabled_by_default()
        => Assert.Equal(All, AgentControl.Enabled(All, new AppSettings()));

    [Fact]
    public void Disabled_agents_are_filtered_but_their_keys_stay_untouched()
    {
        var settings = new AppSettings { DisabledProviders = new() { AiProvider.Grok } };
        Assert.Equal(new[] { AiProvider.Claude, AiProvider.OpenAI }, AgentControl.Enabled(All, settings));
    }

    [Fact]
    public void NextEnabled_skips_disabled_agents_in_rotation()
    {
        var settings = new AppSettings { DisabledProviders = new() { AiProvider.Grok } };
        var rotation = new RoundRobinSelector(All);

        for (var i = 0; i < 6; i++)
            Assert.NotEqual(AiProvider.Grok, AgentControl.NextEnabled(rotation, All.Length, settings));
    }

    [Fact]
    public void NextEnabled_returns_null_when_every_agent_is_disabled()
    {
        var settings = new AppSettings { DisabledProviders = new() { AiProvider.Claude, AiProvider.Grok, AiProvider.OpenAI } };
        var rotation = new RoundRobinSelector(All);
        Assert.Null(AgentControl.NextEnabled(rotation, All.Length, settings));
    }
}
