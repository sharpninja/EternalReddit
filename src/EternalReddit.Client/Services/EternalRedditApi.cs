using System.Net.Http.Json;
using EternalReddit.Shared.Models;

namespace EternalReddit.Client.Services;

/// <summary>Typed client for the server feed API.</summary>
public sealed class EternalRedditApi
{
    private readonly HttpClient _http;
    public EternalRedditApi(HttpClient http) => _http = http;

    public async Task<List<Post>> GetFeedAsync(int count = 50)
        => await _http.GetFromJsonAsync<List<Post>>($"api/posts?count={count}") ?? new();

    public async Task<MeInfo> GetMeAsync()
        => await _http.GetFromJsonAsync<MeInfo>("api/me") ?? new MeInfo(false, null, Array.Empty<string>());

    public Task<HttpResponseMessage> CreatePostAsync(string? title, string body)
        => _http.PostAsJsonAsync("api/posts", new CreatePostBody(title, body));

    public async Task<VoteResult?> VotePostAsync(Guid postId, string dir)
    {
        var res = await _http.PostAsync($"api/posts/{postId}/vote?dir={dir}", null);
        return res.IsSuccessStatusCode ? await res.Content.ReadFromJsonAsync<VoteResult>() : null;
    }

    public async Task<VoteResult?> VoteReplyAsync(Guid postId, Guid replyId, string dir)
    {
        var res = await _http.PostAsync($"api/posts/{postId}/replies/{replyId}/vote?dir={dir}", null);
        return res.IsSuccessStatusCode ? await res.Content.ReadFromJsonAsync<VoteResult>() : null;
    }

    public async Task<int> SharePostAsync(Guid postId, Guid? replyId = null)
    {
        var url = replyId is { } r ? $"api/posts/{postId}/share?replyId={r}" : $"api/posts/{postId}/share";
        var res = await _http.PostAsync(url, null);
        if (!res.IsSuccessStatusCode) return -1;
        var payload = await res.Content.ReadFromJsonAsync<ShareResult>();
        return payload?.ShareCount ?? -1;
    }

    public sealed record CreatePostBody(string? Title, string Body);
    private sealed record ShareResult(int ShareCount, string Url);
}

/// <summary>Current auth state plus the OAuth providers the server has configured.</summary>
public sealed record MeInfo(bool Authenticated, string? Name, string[] Providers);

