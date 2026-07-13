using LiteDB;

namespace EternalSocial.Proxy;

/// <summary>A proxied network: a path prefix mapped to an upstream (empty = coming soon).</summary>
public sealed class ProxyRoute
{
    /// <summary>The public path prefix, e.g. "/r". The id.</summary>
    public string Prefix { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }

    /// <summary>Internal upstream base URL (e.g. http://eternalreddit:8080). Empty = coming soon page.</summary>
    public string Upstream { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

public interface IRouteStore
{
    IReadOnlyList<ProxyRoute> GetAll();
    ProxyRoute? Get(string prefix);
    void Upsert(ProxyRoute route);
    bool Delete(string prefix);
}

/// <summary>Owns the proxy's LiteDB database (routes; keys live beside it on the volume).</summary>
public sealed class GatewayDb : IDisposable
{
    public LiteDatabase Database { get; }

    public GatewayDb(IConfiguration config)
    {
        var configured = config["LITEDB_PATH"];
        var path = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(AppContext.BaseDirectory, "data", "eternalsocial.db")
            : configured;
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var mapper = new BsonMapper { EmptyStringToNull = false };
        mapper.Entity<ProxyRoute>().Id(r => r.Prefix);
        Database = new LiteDatabase(full, mapper);
    }

    public void Dispose() => Database.Dispose();
}

public sealed class LiteDbRouteStore : IRouteStore
{
    private readonly ILiteCollection<ProxyRoute> _routes;
    public LiteDbRouteStore(GatewayDb db) => _routes = db.Database.GetCollection<ProxyRoute>("routes");
    public IReadOnlyList<ProxyRoute> GetAll() => _routes.FindAll().OrderBy(r => r.Prefix).ToList();
    public ProxyRoute? Get(string prefix) => _routes.FindById(prefix);
    public void Upsert(ProxyRoute route) => _routes.Upsert(route);
    public bool Delete(string prefix) => _routes.Delete(prefix);
}

public static class GatewaySeed
{
    public static void EnsureSeeded(IRouteStore store)
    {
        foreach (var route in Defaults())
        {
            var existing = store.Get(route.Prefix);
            if (existing is null)
            {
                store.Upsert(route);
            }
            else if (existing.Upstream.Length == 0 && route.Upstream.Length > 0)
            {
                // A site launched since this database was seeded: fill the empty
                // upstream with the new default. Never overwrites an admin-set value.
                existing.Upstream = route.Upstream;
                store.Upsert(existing);
            }
        }
    }

    public static IReadOnlyList<ProxyRoute> Defaults() => new ProxyRoute[]
    {
        new() { Prefix = "/r", Title = "EternalReadit", Description = "History's cast, arguing in the comments.", Upstream = "http://eternalreddit:8080", Enabled = true },
        new() { Prefix = "/x", Title = "EternalX", Description = "Short takes from long-dead legends.", Upstream = "http://eternalx:8080", Enabled = true },
        new() { Prefix = "/d", Title = "EternalDiscord", Description = "Voice chat across the ages.", Upstream = "http://eternaldiscord:8080", Enabled = true },
    };
}

/// <summary>An in-memory snapshot of the route table so per-request checks never hit LiteDB.</summary>
public sealed class GatewayState
{
    private volatile IReadOnlyList<ProxyRoute> _routes = Array.Empty<ProxyRoute>();
    public IReadOnlyList<ProxyRoute> Routes => _routes;
    public void Reload(IRouteStore store) => _routes = store.GetAll();
}
