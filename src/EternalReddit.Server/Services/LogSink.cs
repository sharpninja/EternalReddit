using EternalReddit.Shared.Models;

namespace EternalReddit.Server.Services;

/// <summary>Bounded, thread-safe ring buffer of recent log lines for the /logs page.</summary>
public sealed class InMemoryLogSink
{
    private const int Capacity = 500;
    private readonly object _gate = new();
    private readonly LinkedList<AppEvent> _events = new();

    public void Add(AppEvent e)
    {
        lock (_gate)
        {
            _events.AddFirst(e); // newest first
            while (_events.Count > Capacity) _events.RemoveLast();
        }
    }

    public IReadOnlyList<AppEvent> Recent(int count)
    {
        lock (_gate) return _events.Take(count <= 0 ? 200 : count).ToList();
    }
}

/// <summary>ILoggerProvider that mirrors every log line into <see cref="InMemoryLogSink"/>.</summary>
public sealed class InMemoryLoggerProvider : ILoggerProvider
{
    private readonly InMemoryLogSink _sink;
    public InMemoryLoggerProvider(InMemoryLogSink sink) => _sink = sink;
    public ILogger CreateLogger(string categoryName) => new InMemoryLogger(_sink, categoryName);
    public void Dispose() { }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }

    private sealed class InMemoryLogger : ILogger
    {
        private readonly InMemoryLogSink _sink;
        private readonly string _full;
        private readonly string _short;

        public InMemoryLogger(InMemoryLogSink sink, string category)
        {
            _sink = sink;
            _full = category;
            // Show the short type name, not the full namespace.
            _short = category.Contains('.') ? category[(category.LastIndexOf('.') + 1)..] : category;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? ex,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            // Keep the app's own logs; from the framework keep only warnings/errors, so
            // per-request HTTP noise (health checks, etc.) doesn't drown the page.
            var isApp = _full.StartsWith("EternalReddit", StringComparison.Ordinal);
            if (!isApp && logLevel < LogLevel.Warning) return;

            var msg = formatter(state, ex);
            if (ex is not null) msg += " | " + ex.Message;
            _sink.Add(new AppEvent(DateTime.UtcNow, logLevel.ToString(), _short, msg));
        }
    }
}
