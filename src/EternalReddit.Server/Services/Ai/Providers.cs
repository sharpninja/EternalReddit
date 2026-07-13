using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EternalReddit.Shared.Models;

namespace EternalReddit.Server.Services.Ai;

/// <summary>Anthropic Claude via the Messages API.</summary>
public sealed class ClaudeProvider : IAiProvider
{
    private readonly IHttpClientFactory _factory;
    private readonly string _key;
    private readonly string _model;

    public ClaudeProvider(IHttpClientFactory factory, IConfiguration config)
    {
        _factory = factory;
        _key = config["ANTHROPIC_API_KEY"] ?? "";
        _model = config["CLAUDE_MODEL"] ?? "claude-haiku-4-5";
    }

    public AiProvider Kind => AiProvider.Claude;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_key);
    public string DefaultModel => _model;

    public async Task<string> CompleteAsync(string system, string user, int maxTokens, string? model = null, CancellationToken ct = default)
    {
        var http = _factory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        req.Headers.Add("x-api-key", _key);
        req.Headers.Add("anthropic-version", "2023-06-01");
        req.Content = JsonContent.Create(new
        {
            model = model ?? _model,
            max_tokens = maxTokens,
            system,
            messages = new[] { new { role = "user", content = user } }
        });

        using var res = await http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        foreach (var block in doc.RootElement.GetProperty("content").EnumerateArray())
            if (block.GetProperty("type").GetString() == "text")
                return block.GetProperty("text").GetString() ?? "";
        return "";
    }

    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default)
    {
        try
        {
            var http = _factory.CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/v1/models?limit=100");
            req.Headers.Add("x-api-key", _key);
            req.Headers.Add("anthropic-version", "2023-06-01");
            using var res = await http.SendAsync(req, ct);
            res.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            return doc.RootElement.GetProperty("data").EnumerateArray()
                .Select(m => m.GetProperty("id").GetString() ?? "")
                .Where(id => id.Length > 0)
                .OrderBy(id => id)
                .ToList();
        }
        catch { return new[] { _model }; }
    }
}

/// <summary>Shared implementation for OpenAI-compatible chat/completions APIs.</summary>
public abstract class OpenAiCompatibleProvider : IAiProvider
{
    private readonly IHttpClientFactory _factory;
    private readonly string _key;
    private readonly string _model;
    private readonly string _endpoint;

    protected OpenAiCompatibleProvider(IHttpClientFactory factory, string key, string model, string endpoint)
    {
        _factory = factory;
        _key = key;
        _model = model;
        _endpoint = endpoint;
    }

    public abstract AiProvider Kind { get; }
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_key);
    public string DefaultModel => _model;

    public async Task<string> CompleteAsync(string system, string user, int maxTokens, string? model = null, CancellationToken ct = default)
    {
        var http = _factory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, _endpoint);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _key);
        req.Content = JsonContent.Create(new
        {
            model = model ?? _model,
            max_tokens = maxTokens,
            messages = new[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user }
            }
        });

        using var res = await http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
    }

    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default)
    {
        try
        {
            var http = _factory.CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Get, _endpoint.Replace("/chat/completions", "/models"));
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _key);
            using var res = await http.SendAsync(req, ct);
            res.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            return doc.RootElement.GetProperty("data").EnumerateArray()
                .Select(m => m.GetProperty("id").GetString() ?? "")
                .Where(id => id.Length > 0)
                .OrderBy(id => id)
                .ToList();
        }
        catch { return new[] { _model }; }
    }
}

public sealed class OpenAiProvider : OpenAiCompatibleProvider
{
    public OpenAiProvider(IHttpClientFactory factory, IConfiguration config)
        : base(factory, config["OPENAI_API_KEY"] ?? "", config["OPENAI_MODEL"] ?? "gpt-4o",
               "https://api.openai.com/v1/chat/completions") { }

    public override AiProvider Kind => AiProvider.OpenAI;
}

public sealed class GrokProvider : OpenAiCompatibleProvider
{
    public GrokProvider(IHttpClientFactory factory, IConfiguration config)
        : base(factory, config["XAI_API_KEY"] ?? "", config["GROK_MODEL"] ?? "grok-3",
               "https://api.x.ai/v1/chat/completions") { }

    public override AiProvider Kind => AiProvider.Grok;
}

/// <summary>Hugging Face Inference API (text-generation models).</summary>
public sealed class HuggingFaceProvider : IAiProvider
{
    private readonly IHttpClientFactory _factory;
    private readonly string _key;
    private readonly string _model;

    public HuggingFaceProvider(IHttpClientFactory factory, IConfiguration config)
    {
        _factory = factory;
        _key = config["HF_API_KEY"] ?? "";
        _model = config["HF_MODEL"] ?? "HuggingFaceH4/zephyr-7b-beta";
    }

    public AiProvider Kind => AiProvider.HuggingFace;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_key);
    public string DefaultModel => _model;

    public async Task<string> CompleteAsync(string system, string user, int maxTokens, string? model = null, CancellationToken ct = default)
    {
        var http = _factory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, $"https://api-inference.huggingface.co/models/{model ?? _model}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _key);
        req.Content = JsonContent.Create(new
        {
            inputs = system + "\n\n" + user,
            parameters = new { max_new_tokens = maxTokens, return_full_text = false }
        });

        using var res = await http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
            return doc.RootElement[0].GetProperty("generated_text").GetString() ?? "";
        return "";
    }

    // The HF hub is effectively unbounded; offer the configured default only.
    public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>(new[] { _model });
}
