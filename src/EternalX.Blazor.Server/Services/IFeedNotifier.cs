namespace EternalX.Blazor.Server.Services;

/// <summary>Pushes "the feed changed" signals to connected clients. Abstracted so
/// PostService and the background service don't depend on SignalR directly.</summary>
public interface IFeedNotifier
{
    Task FeedChangedAsync();
}

/// <summary>No-op notifier for tests and when live updates are not wired.</summary>
public sealed class NullFeedNotifier : IFeedNotifier
{
    public Task FeedChangedAsync() => Task.CompletedTask;
}
