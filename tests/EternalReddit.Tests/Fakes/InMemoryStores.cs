using EternalReddit.Server.Data;
using EternalReddit.Shared.Models;

namespace EternalReddit.Tests.Fakes;

public sealed class InMemoryPostStore : IPostStore
{
    private readonly List<Post> _posts = new();

    public IReadOnlyList<Post> GetRecent(int count = 50)
        => _posts.OrderByDescending(p => p.CreatedUtc).Take(count).ToList();

    public Post? Get(Guid id) => _posts.FirstOrDefault(p => p.Id == id);
    public void Add(Post post) => _posts.Add(post);
    public void Update(Post post) { /* same reference in-memory; nothing to persist */ }
    public bool Delete(Guid id) => _posts.RemoveAll(p => p.Id == id) > 0;

    public int Count => _posts.Count;
}

public sealed class InMemoryUserStore : IUserStore
{
    private readonly Dictionary<string, User> _users = new();
    public User? Get(string id) => _users.TryGetValue(id, out var u) ? u : null;
    public void Upsert(User user) => _users[user.Id] = user;
    public IReadOnlyList<User> GetAll() => _users.Values.ToList();
}

public sealed class InMemoryModerationLogStore : IModerationLogStore
{
    public List<ModerationLog> Logs { get; } = new();
    public void Add(ModerationLog log) => Logs.Add(log);
    public IReadOnlyList<ModerationLog> GetRecent(int count = 100) => Logs;
}
