using EternalReddit.Shared.Models;

namespace EternalReddit.Server.Services.Ai;

/// <summary>
/// One AI backend. Implementations call their provider server-side using a key
/// from configuration/environment. Never invoked from the client.
/// </summary>
public interface IAiProvider
{
    AiProvider Kind { get; }

    /// <summary>True when a key is present so this provider can actually be called.</summary>
    bool IsConfigured { get; }

    /// <summary>The model used when no per-sub override is supplied.</summary>
    string DefaultModel { get; }

    Task<string> CompleteAsync(string system, string user, int maxTokens, string? model = null, string? effort = null, CancellationToken ct = default);

    /// <summary>The models currently available from this provider (best effort; at least the default).</summary>
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default);
}
