using System.Net.Http.Json;
using EternalX.Blazor.Shared.Models;

namespace EternalX.Blazor.Client.Services;

/// <summary>Typed client for the server feed API.</summary>
public sealed class EternalXApi
{
    private readonly HttpClient _http;
    public EternalXApi(HttpClient http) => _http = http;

    public async Task<List<Post>> GetFeedAsync(int count = 50)
        => await _http.GetFromJsonAsync<List<Post>>($"api/posts?count={count}") ?? new();

    public Task<HttpResponseMessage> CreatePostAsync(string? title, string body)
        => _http.PostAsJsonAsync("api/posts", new CreatePostBody(title, body));

    public async Task<bool> VotePostAsync(Guid postId, string dir)
        => (await _http.PostAsync($"api/posts/{postId}/vote?dir={dir}", null)).IsSuccessStatusCode;

    public async Task<bool> VoteReplyAsync(Guid postId, Guid replyId, string dir)
        => (await _http.PostAsync($"api/posts/{postId}/replies/{replyId}/vote?dir={dir}", null)).IsSuccessStatusCode;

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
