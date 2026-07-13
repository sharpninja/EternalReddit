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

    Task<string> CompleteAsync(string system, string user, int maxTokens, string? model = null, CancellationToken ct = default);
}
