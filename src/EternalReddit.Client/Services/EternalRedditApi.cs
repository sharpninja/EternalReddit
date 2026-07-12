using System.Net.Http.Json;
using EternalReddit.Shared.Models;

namespace EternalReddit.Client.Services;

/// <summary>Typed client for the server feed API.</summary>
public sealed class EternalRedditApi
{
    private readonly HttpClient _http;
    public EternalRedditApi(HttpClient http) => _http = http;

    public async Task<List<Post>> GetFeedAsync(string sort = "hot", int count = 50)
        => await _http.GetFromJsonAsync<List<Post>>($"api/posts?sort={sort}&count={count}") ?? new();

    public async Task<Post?> GetPostAsync(Guid id)
    {
        try { return await _http.GetFromJsonAsync<Post>($"api/posts/{id}"); }
        catch { return null; }
    }

    public async Task<MeInfo> GetMeAsync()
        => await _http.GetFromJsonAsync<MeInfo>("api/me") ?? new MeInfo(false, null, Array.Empty<string>());

    public async Task<List<TopPoster>> GetTopPostersAsync(int count = 10)
        => await _http.GetFromJsonAsync<List<TopPoster>>($"api/top-posters?count={count}") ?? new();

    public async Task<List<AppEvent>> GetLogsAsync(int count = 200)
        => await _http.GetFromJsonAsync<List<AppEvent>>($"api/logs?count={count}") ?? new();

    public Task<HttpResponseMessage> CreatePostAsync(string? title, string body)
        => _http.PostAsJsonAsync("api/posts", new CreatePostBody(title, body));

    public async Task<VoteResult?> VotePostAsync(Guid postId, string dir)
        => await ReadVote(await _http.PostAsync($"api/posts/{postId}/vote?dir={dir}", null));

    public async Task<VoteResult?> VoteReplyAsync(Guid postId, Guid replyId, string dir)
        => await ReadVote(await _http.PostAsync($"api/posts/{postId}/replies/{replyId}/vote?dir={dir}", null));

    // Only parse a genuine JSON success body. An unauthenticated vote returns 401
    // (or, historically, an HTML login fallback), which must never be parsed as a
    // VoteResult - that was the "unhandled error" on voting while logged out.
    private static async Task<VoteResult?> ReadVote(HttpResponseMessage res)
        => res.IsSuccessStatusCode && res.Content.Headers.ContentType?.MediaType == "application/json"
            ? await res.Content.ReadFromJsonAsync<VoteResult>()
            : null;

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

