using System.Collections.Concurrent;

namespace EternalSocial.Proxy;

public sealed record GatewayLogEntry(DateTime Utc, string Level, string Category, string Message);

/// <summary>Ring buffer of recent log entries, surfaced on the admin page.</summary>
public sealed class GatewayLogSink
{
    private const int Capacity = 500;
    private readonly ConcurrentQueue<GatewayLogEntry> _entries = new();

    public void Add(GatewayLogEntry entry)
    {
        _entries.Enqueue(entry);
        while (_entries.Count > Capacity && _entries.TryDequeue(out _)) { }
    }

    public IReadOnlyList<GatewayLogEntry> Recent(int count = 200)
        => _entries.Reverse().Take(count).ToList();
}

public sealed class GatewayLoggerProvider : ILoggerProvider
{
    private readonly GatewayLogSink _sink;
    public GatewayLoggerProvider(GatewayLogSink sink) => _sink = sink;
    public ILogger CreateLogger(string categoryName) => new SinkLogger(_sink, categoryName);
    public void Dispose() { }

    private sealed class SinkLogger : ILogger
    {
        private readonly GatewayLogSink _sink;
        private readonly string _category;
        public SinkLogger(GatewayLogSink sink, string category) { _sink = sink; _category = category; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var message = formatter(state, exception) + (exception is null ? "" : $" :: {exception.GetType().Name}: {exception.Message}");
            _sink.Add(new GatewayLogEntry(DateTime.UtcNow, logLevel.ToString(), Short(_category), message));
        }
        private static string Short(string category)
            => category.LastIndexOf('.') is var i and >= 0 ? category[(i + 1)..] : category;
    }
}
