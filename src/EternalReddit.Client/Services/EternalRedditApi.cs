using System.Net.Http.Json;
using EternalReddit.Shared.Models;

namespace EternalReddit.Client.Services;

/// <summary>Typed client for the server feed API.</summary>
public sealed class EternalRedditApi
{
    private readonly HttpClient _http;
    public EternalRedditApi(HttpClient http) => _http = http;

    public async Task<List<Post>> GetFeedAsync(string sort = "hot", int count = 50, string? sub = null)
        => await _http.GetFromJsonAsync<List<Post>>(
            $"api/posts?sort={sort}&count={count}{(string.IsNullOrEmpty(sub) ? "" : $"&sub={Uri.EscapeDataString(sub)}")}") ?? new();

    public async Task<List<Community>> GetCommunitiesAsync()
        => await _http.GetFromJsonAsync<List<Community>>("api/communities") ?? new();

    public async Task<Post?> GetPostAsync(Guid id)
    {
        try { return await _http.GetFromJsonAsync<Post>($"api/posts/{id}"); }
        catch { return null; }
    }

    public async Task<MeInfo> GetMeAsync()
        => await _http.GetFromJsonAsync<MeInfo>("api/me") ?? new MeInfo(false, null, Array.Empty<string>(), false);

    public async Task<List<Post>?> GetMyPostsAsync()
    {
        var res = await _http.GetAsync("api/me/posts");
        return res.IsSuccessStatusCode ? await res.Content.ReadFromJsonAsync<List<Post>>() : null;
    }

    public async Task<UserProfile?> GetUserProfileAsync(string name)
    {
        try { return await _http.GetFromJsonAsync<UserProfile>($"api/users/{Uri.EscapeDataString(name)}"); }
        catch { return null; }
    }

    public async Task<List<TopPoster>> GetTopPostersAsync(int count = 10)
        => await _http.GetFromJsonAsync<List<TopPoster>>($"api/top-posters?count={count}") ?? new();

    public async Task<List<AppEvent>> GetLogsAsync(int count = 200)
        => await _http.GetFromJsonAsync<List<AppEvent>>($"api/logs?count={count}") ?? new();

    public Task<HttpResponseMessage> CreatePostAsync(string? title, string body, string? community = null)
        => _http.PostAsJsonAsync("api/posts", new CreatePostBody(title, body, community));

    public Task<HttpResponseMessage> AddReplyAsync(Guid postId, Guid? parentReplyId, string body)
        => _http.PostAsJsonAsync($"api/posts/{postId}/replies", new AddReplyBody(parentReplyId, body));

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

    // --- Admin (all require the Admin policy server-side) ---

    public async Task<List<Community>> AdminGetCommunitiesAsync()
        => await _http.GetFromJsonAsync<List<Community>>("api/admin/communities") ?? new();
    public Task<HttpResponseMessage> AdminSaveCommunityAsync(Community c)
        => _http.PutAsJsonAsync($"api/admin/communities/{Uri.EscapeDataString(c.Slug)}", c);
    public Task<HttpResponseMessage> AdminDeleteCommunityAsync(string slug)
        => _http.DeleteAsync($"api/admin/communities/{Uri.EscapeDataString(slug)}");

    public async Task<List<PeerGroup>> AdminGetPeerGroupsAsync()
        => await _http.GetFromJsonAsync<List<PeerGroup>>("api/admin/peer-groups") ?? new();
    public Task<HttpResponseMessage> AdminSavePeerGroupAsync(PeerGroup g)
        => _http.PutAsJsonAsync($"api/admin/peer-groups/{Uri.EscapeDataString(g.Slug)}", g);
    public Task<HttpResponseMessage> AdminDeletePeerGroupAsync(string slug)
        => _http.DeleteAsync($"api/admin/peer-groups/{Uri.EscapeDataString(slug)}");

    public async Task<List<Figure>> AdminGetFiguresAsync()
        => await _http.GetFromJsonAsync<List<Figure>>("api/admin/figures") ?? new();
    public Task<HttpResponseMessage> AdminSaveFigureAsync(Figure f)
        => _http.PutAsJsonAsync($"api/admin/figures/{Uri.EscapeDataString(f.Name)}", f);
    public Task<HttpResponseMessage> AdminDeleteFigureAsync(string name)
        => _http.DeleteAsync($"api/admin/figures/{Uri.EscapeDataString(name)}");

    public Task<HttpResponseMessage> AdminDeletePostAsync(Guid id)
        => _http.DeleteAsync($"api/admin/posts/{id}");
    public Task<HttpResponseMessage> AdminDeleteReplyAsync(Guid postId, Guid replyId)
        => _http.DeleteAsync($"api/admin/posts/{postId}/replies/{replyId}");

    public Task<HttpResponseMessage> AdminBanAsync(string userId, string? name, string? reason)
        => _http.PostAsJsonAsync("api/admin/users/ban", new BanBody(userId, name, reason));
    public Task<HttpResponseMessage> AdminUnbanAsync(string userId)
        => _http.PostAsJsonAsync("api/admin/users/unban", new BanBody(userId, null, null));
    public async Task<List<User>> AdminGetBannedAsync()
        => await _http.GetFromJsonAsync<List<User>>("api/admin/users/banned") ?? new();

    public async Task<AppSettings> AdminGetSettingsAsync()
        => await _http.GetFromJsonAsync<AppSettings>("api/admin/settings") ?? new();
    public Task<HttpResponseMessage> AdminSaveSettingsAsync(AppSettings s)
        => _http.PutAsJsonAsync("api/admin/settings", s);

    public async Task<AdminStats?> AdminGetStatsAsync()
    {
        try { return await _http.GetFromJsonAsync<AdminStats>("api/admin/stats"); }
        catch { return null; }
    }

    public async Task<List<ModerationLog>> AdminGetModerationLogAsync(int count = 100)
        => await _http.GetFromJsonAsync<List<ModerationLog>>($"api/admin/moderation-log?count={count}") ?? new();

    public Task<HttpResponseMessage> AdminSeedAsync(string figure, string? sub = null)
        => _http.PostAsync($"api/seed?figure={Uri.EscapeDataString(figure)}{(string.IsNullOrEmpty(sub) ? "" : $"&sub={Uri.EscapeDataString(sub)}")}", null);

    public sealed record CreatePostBody(string? Title, string Body, string? Community = null);
    public sealed record AddReplyBody(Guid? ParentReplyId, string Body);
    public sealed record BanBody(string UserId, string? Name, string? Reason);
    private sealed record ShareResult(int ShareCount, string Url);
}

/// <summary>Admin dashboard stats (mirrors /api/admin/stats).</summary>
public sealed record AdminStats(int Posts, int Comments, int HumanComments, int AiComments, int BannedUsers,
    int Figures, int Communities, AiProvider[] Providers, AppSettings Settings);

/// <summary>Current auth state plus the OAuth providers the server has configured.</summary>
public sealed record MeInfo(bool Authenticated, string? Name, string[] Providers, bool IsAdmin);

