using EternalReddit.Server.Services.Ai;
using EternalReddit.Server.Services.Moderation;
using EternalReddit.Shared.Models;

namespace EternalReddit.Tests.Fakes;

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
    public string? LastSystem { get; private set; }
    public string? LastModel { get; private set; }

    public Task<string> CompleteAsync(string system, string user, int maxTokens, string? model = null, CancellationToken ct = default)
    {
        LastSystem = system;
        LastUser = user;
        LastModel = model;
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
