using System.Text.Json;
using EternalReddit.Shared.Models;

namespace EternalReddit.Server.Services.Ai;

/// <summary>An AI-drafted original post before it enters the feed.</summary>
public sealed record PostDraft(string Figure, string Title, string Body);

/// <summary>Turns a post (and its thread so far) into one in-character AI reply.</summary>
public interface IReplyGenerator
{
    /// <summary>Providers that are configured and can be used, in a stable order.</summary>
    IReadOnlyList<AiProvider> Available { get; }

    Task<Reply> GenerateReplyAsync(Post post, AiProvider provider, bool isBackground = false, CancellationToken ct = default);

    /// <summary>Draft an original post in the voice of a figure (used when the feed goes quiet).</summary>
    Task<PostDraft> GeneratePostAsync(AiProvider provider, CancellationToken ct = default);
}

public sealed class ReplyGenerator : IReplyGenerator
{
    private const string SystemPrompt =
        "You write one comment in a satirical Reddit thread on r/AllOfHistory, where historical, " +
        "legendary, and mythical figures post as contemporaries. Stay in character, grounded in the " +
        "figure's real personality and feuds, and reply to other figures for unlikely but on-point " +
        "crossovers. Never build humor on atrocity, genocide, slavery, or violent conquest, and do not " +
        "feature figures whose primary legacy is such. Do not fabricate real quotes. Reply with ONLY a " +
        "JSON object: {\"figure\":\"Name\",\"body\":\"the comment, 1-3 sentences\"}.";

    private const string PostSystemPrompt =
        "You are a historical, legendary, or mythical figure posting on r/AllOfHistory, where such " +
        "figures post as contemporaries. Write ONE original post in character: a question, hot take, or " +
        "wry observation about modern life or history, grounded in the figure's real personality. Never " +
        "build humor on atrocity, genocide, slavery, or violent conquest, and do not feature figures whose " +
        "primary legacy is such. Do not fabricate real quotes. Reply with ONLY a JSON object: " +
        "{\"figure\":\"Name\",\"title\":\"a short title\",\"body\":\"1-3 sentences\"}.";

    private readonly IReadOnlyDictionary<AiProvider, IAiProvider> _providers;

    public ReplyGenerator(IEnumerable<IAiProvider> providers)
    {
        var configured = providers.Where(p => p.IsConfigured).ToArray();
        _providers = configured.ToDictionary(p => p.Kind);
        Available = configured.Select(p => p.Kind).ToArray();
    }

    public IReadOnlyList<AiProvider> Available { get; }

    public async Task<Reply> GenerateReplyAsync(Post post, AiProvider provider, bool isBackground = false, CancellationToken ct = default)
    {
        if (!_providers.TryGetValue(provider, out var ai))
            throw new InvalidOperationException($"Provider {provider} is not configured.");

        var text = await ai.CompleteAsync(SystemPrompt, BuildUserPrompt(post), 400, ct);
        var (figure, body) = ParseReply(text);

        return new Reply
        {
            Figure = figure,
            Provider = provider,
            Body = body,
            IsBackground = isBackground,
            CreatedUtc = DateTime.UtcNow
        };
    }

    private static string BuildUserPrompt(Post post)
    {
        var thread = string.Join("\n", post.Replies.Select(r => $"{r.Figure}: {r.Body}"));
        var header = string.IsNullOrWhiteSpace(post.Title) ? post.Body : $"{post.Title}\n{post.Body}";
        return thread.Length == 0
            ? $"POST: {header}\n\nWrite the first comment."
            : $"POST: {header}\n\nComments so far:\n{thread}\n\nWrite the next comment as a different figure.";
    }

    private static (string Figure, string Body) ParseReply(string text)
    {
        var trimmed = (text ?? "").Replace("```json", "").Replace("```", "").Trim();
        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed[start..(end + 1)]);
                var root = doc.RootElement;
                var figure = root.TryGetProperty("figure", out var f) ? f.GetString() : null;
                var body = root.TryGetProperty("body", out var b) ? b.GetString() : null;
                if (!string.IsNullOrWhiteSpace(body))
                    return (string.IsNullOrWhiteSpace(figure) ? "A Historical Figure" : figure!, body!);
            }
            catch (JsonException)
            {
                // fall through to plain-text handling
            }
        }
        return ("A Historical Figure", string.IsNullOrWhiteSpace(text) ? "" : text.Trim());
    }

    public async Task<PostDraft> GeneratePostAsync(AiProvider provider, CancellationToken ct = default)
    {
        if (!_providers.TryGetValue(provider, out var ai))
            throw new InvalidOperationException($"Provider {provider} is not configured.");

        var text = await ai.CompleteAsync(PostSystemPrompt, "Write your original post now.", 400, ct);
        return ParsePost(text);
    }

    private static PostDraft ParsePost(string text)
    {
        var trimmed = (text ?? "").Replace("```json", "").Replace("```", "").Trim();
        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed[start..(end + 1)]);
                var root = doc.RootElement;
                var figure = root.TryGetProperty("figure", out var f) ? f.GetString() : null;
                var title = root.TryGetProperty("title", out var t) ? t.GetString() : null;
                var body = root.TryGetProperty("body", out var b) ? b.GetString() : null;
                if (!string.IsNullOrWhiteSpace(body))
                    return new PostDraft(
                        string.IsNullOrWhiteSpace(figure) ? "A Historical Figure" : figure!,
                        string.IsNullOrWhiteSpace(title) ? "" : title!,
                        body!);
            }
            catch (JsonException)
            {
                // fall through to plain-text handling
            }
        }
        return new PostDraft("A Historical Figure", "", string.IsNullOrWhiteSpace(text) ? "" : text.Trim());
    }
}
