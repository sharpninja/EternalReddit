using EternalX.Blazor.Server.Services.Ai;
using EternalX.Blazor.Server.Services.Moderation;
using EternalX.Blazor.Shared.Models;

namespace EternalX.Blazor.Tests.Fakes;

/// <summary>Returns a canned completion; records the last call for assertions.</summary>
public sealed class FakeAiProvider : IAiProvider
{
    private readonly string _response;

    public FakeAiProvider(AiProvider kind, string response, bool configured = true)
    {
        Kind = kind;
        _response = response;
        IsConfigured = configured;
    }

    public AiProvider Kind { get; }
    public bool IsConfigured { get; }
    public string? LastUser { get; private set; }

    public Task<string> CompleteAsync(string system, string user, int maxTokens, CancellationToken ct = default)
    {
        LastUser = user;
        return Task.FromResult(_response);
    }
}

/// <summary>Always returns the configured verdict (used to isolate Moderator logic).</summary>
public sealed class StubClassifier : IModerationClassifier
{
    private readonly ModerationVerdict _verdict;
    public StubClassifier(ModerationVerdict verdict) => _verdict = verdict;
    public Task<ModerationVerdict> ClassifyAsync(string content, CancellationToken ct = default)
        => Task.FromResult(_verdict);
}
