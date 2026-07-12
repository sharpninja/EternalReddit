using EternalReddit.Shared.Models;

namespace EternalReddit.Server.Services.Ai;

/// <summary>
/// Round-robins over the available AI providers so a reply thread is written by
/// a rotating cast. Optionally starts at a specific provider (e.g. Claude, the
/// configured default). Thread-safe.
/// </summary>
public sealed class RoundRobinSelector
{
    private readonly AiProvider[] _providers;
    private readonly object _gate = new();
    private int _index;

    public RoundRobinSelector(IEnumerable<AiProvider> available, AiProvider? startWith = null)
    {
        ArgumentNullException.ThrowIfNull(available);
        _providers = available.Distinct().ToArray();
        if (_providers.Length == 0)
            throw new ArgumentException("At least one provider is required.", nameof(available));

        if (startWith is { } sw)
        {
            var i = Array.IndexOf(_providers, sw);
            if (i < 0)
                throw new ArgumentException("startWith must be one of the available providers.", nameof(startWith));
            _index = i;
        }
    }

    public AiProvider Next()
    {
        lock (_gate)
        {
            var provider = _providers[_index];
            _index = (_index + 1) % _providers.Length;
            return provider;
        }
    }
}
