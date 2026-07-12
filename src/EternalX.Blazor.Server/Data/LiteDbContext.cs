using LiteDB;

namespace EternalX.Blazor.Server.Data;

/// <summary>
/// Owns the single shared <see cref="LiteDatabase"/> instance (LiteDB is
/// thread-safe for a shared connection). Registered as a singleton.
/// </summary>
public sealed class LiteDbContext : IDisposable
{
    public LiteDatabase Database { get; }

    public LiteDbContext(IConfiguration config)
        : this(ResolvePath(config["LITEDB_PATH"])) { }

    public LiteDbContext(string dbPath)
    {
        var full = Path.GetFullPath(dbPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        Database = new LiteDatabase(full, BuildMapper());
    }

    // LiteDB otherwise returns DateTime as Local on read (shifting the value).
    // Persist as UTC ticks so timestamps round-trip as UTC deterministically.
    private static BsonMapper BuildMapper()
    {
        var mapper = new BsonMapper();
        mapper.RegisterType<DateTime>(
            dt => dt.ToUniversalTime().Ticks,
            bson => new DateTime(bson.AsInt64, DateTimeKind.Utc));
        return mapper;
    }

    private static string ResolvePath(string? configured)
        => string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(AppContext.BaseDirectory, "data", "eternalx.db")
            : configured;

    public void Dispose() => Database.Dispose();
}
