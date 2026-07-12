using EternalReddit.Shared.Models;

namespace EternalReddit.Server.Services;

/// <summary>Pushes live signals to connected clients. Abstracted so PostService and
/// the background services don't depend on SignalR directly.</summary>
public interface IFeedNotifier
{
    /// <summary>A post or comment was added/removed - clients reload the feed.</summary>
    Task FeedChangedAsync();

    /// <summary>A post or comment's score changed - clients update just that tally.</summary>
    Task ScoreChangedAsync(ScoreUpdate update);
}

/// <summary>No-op notifier for tests and when live updates are not wired.</summary>
public sealed class NullFeedNotifier : IFeedNotifier
{
    public Task FeedChangedAsync() => Task.CompletedTask;
    public Task ScoreChangedAsync(ScoreUpdate update) => Task.CompletedTask;
}
