using EternalReddit.Server.Services.Ai;
using EternalReddit.Shared.Models;

namespace EternalReddit.Server.Services;

/// <summary>
/// Pure guards for the admin's per-agent enable toggle (like <see cref="AiFeedControl"/>).
/// A disabled agent keeps its API key; it is only skipped when picking who writes.
/// </summary>
public static class AgentControl
{
    public static IReadOnlyList<AiProvider> Enabled(IReadOnlyList<AiProvider> available, AppSettings settings)
        => available.Where(p => !settings.DisabledProviders.Contains(p)).ToArray();

    /// <summary>Advances the rotation until an enabled agent turns up; null when all are benched.</summary>
    public static AiProvider? NextEnabled(RoundRobinSelector rotation, int poolSize, AppSettings settings)
    {
        for (var i = 0; i < poolSize; i++)
        {
            var provider = rotation.Next();
            if (!settings.DisabledProviders.Contains(provider)) return provider;
        }
        return null;
    }
}
