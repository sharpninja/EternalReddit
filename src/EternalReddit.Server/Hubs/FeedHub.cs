using EternalReddit.Server.Services;
using Microsoft.AspNetCore.SignalR;

namespace EternalReddit.Server.Hubs;

/// <summary>SignalR hub clients subscribe to for live feed updates.</summary>
public sealed class FeedHub : Hub { }

/// <summary>Broadcasts a "FeedChanged" event to all connected clients.</summary>
public sealed class SignalRFeedNotifier : IFeedNotifier
{
    private readonly IHubContext<FeedHub> _hub;
    public SignalRFeedNotifier(IHubContext<FeedHub> hub) => _hub = hub;
    public Task FeedChangedAsync() => _hub.Clients.All.SendAsync("FeedChanged");
}
