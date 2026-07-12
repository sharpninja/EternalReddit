using EternalReddit.Server.Data;
using EternalReddit.Shared.Models;

namespace EternalReddit.Tests.Fakes;

public sealed class InMemoryFigureStore : IFigureStore
{
    private readonly Dictionary<string, Figure> _figures = new();
    public IReadOnlyList<Figure> GetAll() => _figures.Values.ToList();
    public Figure? Get(string name) => _figures.TryGetValue(name, out var f) ? f : null;
    public void Upsert(Figure figure) => _figures[figure.Name] = figure;
    public void Delete(string name) => _figures.Remove(name);
    public bool Any() => _figures.Count > 0;
}

public sealed class InMemoryPeerGroupStore : IPeerGroupStore
{
    private readonly Dictionary<string, PeerGroup> _groups = new();
    public IReadOnlyList<PeerGroup> GetAll() => _groups.Values.ToList();
    public PeerGroup? Get(string slug) => _groups.TryGetValue(slug, out var g) ? g : null;
    public void Upsert(PeerGroup group) => _groups[group.Slug] = group;
    public void Delete(string slug) => _groups.Remove(slug);
    public bool Any() => _groups.Count > 0;
}

public sealed class InMemoryCommunityStore : ICommunityStore
{
    private readonly Dictionary<string, Community> _communities = new();
    public IReadOnlyList<Community> GetAll() => _communities.Values.ToList();
    public Community? Get(string slug) => _communities.TryGetValue(slug, out var c) ? c : null;
    public void Upsert(Community community) => _communities[community.Slug] = community;
    public void Delete(string slug) => _communities.Remove(slug);
    public bool Any() => _communities.Count > 0;
}

public sealed class InMemorySettingsStore : ISettingsStore
{
    private AppSettings _settings = new();
    public AppSettings Get() => _settings;
    public void Save(AppSettings settings) => _settings = settings;
}
