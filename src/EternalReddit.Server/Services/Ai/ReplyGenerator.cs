using System.Text;
using System.Text.Json;
using EternalReddit.Shared.Models;

namespace EternalReddit.Server.Services.Ai;

/// <summary>An AI-drafted original post (title + body) before it enters the feed.</summary>
public sealed record PostDraft(string Title, string Body);

/// <summary>
/// Generates in-character comment/post text. The <b>figure is chosen by the caller</b>
/// (only approved figures speak); the model only supplies the words.
/// </summary>
public interface IReplyGenerator
{
    /// <summary>Providers that are configured and can be used, in a stable order.</summary>
    IReadOnlyList<AiProvider> Available { get; }

    /// <summary>
    /// One comment body in <paramref name="figure"/>'s voice. When <paramref name="parentFigure"/>
    /// is set the reply addresses them directly, given the ancestor <paramref name="branch"/>
    /// (root → parent) for context.
    /// </summary>
    Task<string> GenerateReplyBodyAsync(Post post, IReadOnlyList<Reply> branch, string figure, string? parentFigure, AiProvider provider, CancellationToken ct = default);

    /// <summary>An original post in <paramref name="figure"/>'s voice.</summary>
    Task<PostDraft> GeneratePostAsync(string figure, AiProvider provider, CancellationToken ct = default);
}

public sealed class ReplyGenerator : IReplyGenerator
{
    private readonly IReadOnlyDictionary<AiProvider, IAiProvider> _providers;

    public ReplyGenerator(IEnumerable<IAiProvider> providers)
    {
        var configured = providers.Where(p => p.IsConfigured).ToArray();
        _providers = configured.ToDictionary(p => p.Kind);
        Available = configured.Select(p => p.Kind).ToArray();
    }

    public IReadOnlyList<AiProvider> Available { get; }

    public async Task<string> GenerateReplyBodyAsync(Post post, IReadOnlyList<Reply> branch, string figure, string? parentFigure, AiProvider provider, CancellationToken ct = default)
    {
        var ai = Resolve(provider);
        var text = await ai.CompleteAsync(ReplySystem(figure, parentFigure), BuildBranchPrompt(post, branch, figure, parentFigure), 400, ct);
        return CleanText(text);
    }

    public async Task<PostDraft> GeneratePostAsync(string figure, AiProvider provider, CancellationToken ct = default)
    {
        var ai = Resolve(provider);
        var system =
            $"You are {figure}, writing an original post on r/AllOfHistory, where historical, legendary, and " +
            "mythical figures post as contemporaries. Write a short post in character - a question, hot take, " +
            $"or wry observation - grounded in {figure}'s real personality and era. Never build humor on " +
            "atrocity, genocide, slavery, or violent conquest. Do not fabricate real quotes. Reply with ONLY " +
            "a JSON object: {\"title\":\"a short title\",\"body\":\"1-3 sentences\"}.";
        return ParsePost(await ai.CompleteAsync(system, "Write your post now.", 400, ct));
    }

    private IAiProvider Resolve(AiProvider provider)
        => _providers.TryGetValue(provider, out var ai) ? ai
            : throw new InvalidOperationException($"Provider {provider} is not configured.");

    private static string ReplySystem(string figure, string? parentFigure)
        => $"You are {figure}, commenting on r/AllOfHistory, where historical, legendary, and mythical figures " +
           $"talk as contemporaries. Stay in character, grounded in {figure}'s real personality, era, and " +
           "rivalries. " +
           (parentFigure is null
               ? "Write a top-level comment on the post. "
               : $"You are replying directly to {parentFigure}: address them by name and respond to what they " +
                 "actually said, building on the whole conversation above. ") +
           "Keep it to 1-3 sentences. Never build humor on atrocity, genocide, slavery, or violent conquest. " +
           "Do not fabricate real quotes. Reply with ONLY your comment text - no name label, no surrounding quotes, no JSON.";

    private static string BuildBranchPrompt(Post post, IReadOnlyList<Reply> branch, string figure, string? parentFigure)
    {
        var sb = new StringBuilder();
        sb.Append("POST: ").Append(string.IsNullOrWhiteSpace(post.Title) ? post.Body : $"{post.Title}\n{post.Body}").Append('\n');
        if (branch.Count > 0)
        {
            sb.Append("\nConversation so far (oldest first):\n");
            foreach (var r in branch)
                sb.Append(r.Figure).Append(": ").Append(r.Body).Append('\n');
            sb.Append($"\nNow write {figure}'s reply to {parentFigure}.");
        }
        else
        {
            sb.Append($"\nWrite {figure}'s comment on this post.");
        }
        return sb.ToString();
    }

    private static string CleanText(string text)
    {
        var t = (text ?? "").Trim();
        if (t.StartsWith("{") && t.Contains("\"body\""))
        {
            try { using var d = JsonDocument.Parse(t); if (d.RootElement.TryGetProperty("body", out var b)) t = b.GetString() ?? t; }
            catch { /* not JSON, use as-is */ }
        }
        t = t.Trim();
        if (t.Length >= 2 && t[0] == '"' && t[^1] == '"') t = t[1..^1].Trim();
        return t;
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
                var title = root.TryGetProperty("title", out var t) ? t.GetString() : null;
                var body = root.TryGetProperty("body", out var b) ? b.GetString() : null;
                if (!string.IsNullOrWhiteSpace(body))
                    return new PostDraft(string.IsNullOrWhiteSpace(title) ? "Untitled" : title!, body!);
            }
            catch (JsonException) { /* fall through */ }
        }
        return new PostDraft("Untitled", string.IsNullOrWhiteSpace(text) ? "" : text.Trim());
    }
}
