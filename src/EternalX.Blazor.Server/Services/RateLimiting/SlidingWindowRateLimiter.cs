namespace EternalX.Blazor.Server.Services.RateLimiting;

/// <param name="Allowed">True if the request may proceed.</param>
/// <param name="RetryAfter">When blocked, how long until the caller may retry.</param>
public readonly record struct RateLimitResult(bool Allowed, TimeSpan RetryAfter);

/// <summary>Per-key request throttle, checked before any business logic.</summary>
public interface IRateLimiter
{
    RateLimitResult Check(string key);
}

/// <summary>
/// In-memory sliding-window limiter. For the "1 post/min per IP" rule use
/// limit=1, window=60s. Thread-safe for concurrent request handling.
/// </summary>
public sealed class SlidingWindowRateLimiter : IRateLimiter
{
    private readonly IClock _clock;
    private readonly int _limit;
    private readonly TimeSpan _window;
    private readonly Dictionary<string, Queue<DateTimeOffset>> _hits = new();
    private readonly object _gate = new();

    public SlidingWindowRateLimiter(IClock clock, int limit, TimeSpan window)
    {
        if (limit < 1) throw new ArgumentOutOfRangeException(nameof(limit));
        if (window <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(window));
        _clock = clock;
        _limit = limit;
        _window = window;
    }

    public RateLimitResult Check(string key)
    {
        var now = _clock.UtcNow;
        lock (_gate)
        {
            if (!_hits.TryGetValue(key, out var q))
            {
                q = new Queue<DateTimeOffset>();
                _hits[key] = q;
            }

            while (q.Count > 0 && now - q.Peek() >= _window)
                q.Dequeue();

            if (q.Count < _limit)
            {
                q.Enqueue(now);
                return new RateLimitResult(true, TimeSpan.Zero);
            }

            var retryAfter = _window - (now - q.Peek());
            if (retryAfter < TimeSpan.Zero) retryAfter = TimeSpan.Zero;
            return new RateLimitResult(false, retryAfter);
        }
    }
}
