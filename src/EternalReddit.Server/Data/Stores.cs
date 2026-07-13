using EternalReddit.Shared.Models;
using LiteDB;

namespace EternalReddit.Server.Data;

public interface IPostStore
{
    IReadOnlyList<Post> GetRecent(int count = 50);
    Post? Get(Guid id);
    void Add(Post post);
    void Update(Post post);
    bool Delete(Guid id);
    void Clear();
}

public interface IUserStore
{
    User? Get(string id);
    void Upsert(User user);
    IReadOnlyList<User> GetAll();
    void Clear();
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
        _posts.EnsureIndex(p => p.Community);
    }

    public IReadOnlyList<Post> GetRecent(int count = 50)
        => _posts.Query().OrderByDescending(p => p.CreatedUtc).Limit(count).ToList();

    public Post? Get(Guid id) => _posts.FindById(id);
    public void Add(Post post) => _posts.Insert(post);
    public void Update(Post post) => _posts.Update(post);
    public bool Delete(Guid id) => _posts.Delete(id);
    public void Clear() => _posts.DeleteAll();
}

public sealed class LiteDbUserStore : IUserStore
{
    private readonly ILiteCollection<User> _users;

    public LiteDbUserStore(LiteDbContext ctx) => _users = ctx.Database.GetCollection<User>("users");

    public User? Get(string id) => _users.FindById(id);
    public void Upsert(User user) => _users.Upsert(user);
    public IReadOnlyList<User> GetAll() => _users.FindAll().ToList();
    public void Clear() => _users.DeleteAll();
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

// --- Data-driven roster + communities (seeded on first run; admin-managed thereafter) ---

public interface IFigureStore
{
    IReadOnlyList<Figure> GetAll();
    Figure? Get(string name);
    void Upsert(Figure figure);
    void Delete(string name);
    bool Any();
}

public interface IPeerGroupStore
{
    IReadOnlyList<PeerGroup> GetAll();
    PeerGroup? Get(string slug);
    void Upsert(PeerGroup group);
    void Delete(string slug);
    bool Any();
}

public interface ICommunityStore
{
    IReadOnlyList<Community> GetAll();
    Community? Get(string slug);
    void Upsert(Community community);
    void Delete(string slug);
    bool Any();
}

public interface ISettingsStore
{
    AppSettings Get();
    void Save(AppSettings settings);
}

public sealed class LiteDbFigureStore : IFigureStore
{
    private readonly ILiteCollection<Figure> _figures;
    public LiteDbFigureStore(LiteDbContext ctx) => _figures = ctx.Database.GetCollection<Figure>("figures");
    public IReadOnlyList<Figure> GetAll() => _figures.FindAll().ToList();
    public Figure? Get(string name) => _figures.FindById(name);
    public void Upsert(Figure figure) => _figures.Upsert(figure);
    public void Delete(string name) => _figures.Delete(name);
    public bool Any() => _figures.Count() > 0;
}

public sealed class LiteDbPeerGroupStore : IPeerGroupStore
{
    private readonly ILiteCollection<PeerGroup> _groups;
    public LiteDbPeerGroupStore(LiteDbContext ctx) => _groups = ctx.Database.GetCollection<PeerGroup>("peer_groups");
    public IReadOnlyList<PeerGroup> GetAll() => _groups.FindAll().ToList();
    public PeerGroup? Get(string slug) => _groups.FindById(slug);
    public void Upsert(PeerGroup group) => _groups.Upsert(group);
    public void Delete(string slug) => _groups.Delete(slug);
    public bool Any() => _groups.Count() > 0;
}

public sealed class LiteDbCommunityStore : ICommunityStore
{
    private readonly ILiteCollection<Community> _communities;
    public LiteDbCommunityStore(LiteDbContext ctx) => _communities = ctx.Database.GetCollection<Community>("communities");
    public IReadOnlyList<Community> GetAll() => _communities.FindAll().ToList();
    public Community? Get(string slug) => _communities.FindById(slug);
    public void Upsert(Community community) => _communities.Upsert(community);
    public void Delete(string slug) => _communities.Delete(slug);
    public bool Any() => _communities.Count() > 0;
}

public sealed class LiteDbSettingsStore : ISettingsStore
{
    private readonly ILiteCollection<AppSettings> _settings;
    public LiteDbSettingsStore(LiteDbContext ctx) => _settings = ctx.Database.GetCollection<AppSettings>("settings");
    public AppSettings Get() => _settings.FindById(1) ?? new AppSettings();
    public void Save(AppSettings settings) => _settings.Upsert(1, settings);
}
