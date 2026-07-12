using EternalReddit.Shared.Models;
using LiteDB;

namespace EternalReddit.Server.Data;

public interface IPostStore
{
    IReadOnlyList<Post> GetRecent(int count = 50);
    Post? Get(Guid id);
    void Add(Post post);
    void Update(Post post);
}

public interface IUserStore
{
    User? Get(string id);
    void Upsert(User user);
}

public interface IModerationLogStore
{
    void Add(ModerationLog log);
    IReadOnlyList<ModerationLog> GetRecent(int count = 100);
}

public sealed class LiteDbPostStore : IPostStore
{
    private readonly ILiteCollection<Post> _posts;

    public LiteDbPostStore(LiteDbContext ctx)
    {
        _posts = ctx.Database.GetCollection<Post>("posts");
        _posts.EnsureIndex(p => p.CreatedUtc);
    }

    public IReadOnlyList<Post> GetRecent(int count = 50)
        => _posts.Query().OrderByDescending(p => p.CreatedUtc).Limit(count).ToList();

    public Post? Get(Guid id) => _posts.FindById(id);
    public void Add(Post post) => _posts.Insert(post);
    public void Update(Post post) => _posts.Update(post);
}

public sealed class LiteDbUserStore : IUserStore
{
    private readonly ILiteCollection<User> _users;

    public LiteDbUserStore(LiteDbContext ctx) => _users = ctx.Database.GetCollection<User>("users");

    public User? Get(string id) => _users.FindById(id);
    public void Upsert(User user) => _users.Upsert(user);
}

public sealed class LiteDbModerationLogStore : IModerationLogStore
{
    private readonly ILiteCollection<ModerationLog> _logs;

    public LiteDbModerationLogStore(LiteDbContext ctx)
    {
        _logs = ctx.Database.GetCollection<ModerationLog>("moderation_logs");
        _logs.EnsureIndex(l => l.CreatedUtc);
    }

    public void Add(ModerationLog log) => _logs.Insert(log);

    public IReadOnlyList<ModerationLog> GetRecent(int count = 100)
        => _logs.Query().OrderByDescending(l => l.CreatedUtc).Limit(count).ToList();
}
