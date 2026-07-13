using EternalReddit.Server.Data;
using EternalReddit.Shared.Models;

namespace EternalReddit.Server.Services.Ai;

/// <summary>
/// Data-driven access to the approved cast: validation, personas, and group-scoped
/// random selection. Backed by <see cref="IFigureStore"/>, so admin edits apply live.
/// </summary>
public interface IRosterService
{
    /// <summary>True if the name is a known figure (enabled or not) - drives the purge.</summary>
    bool IsApproved(string name);

    /// <summary>The figure's persona text, or null if the name isn't a figure.</summary>
    string? Persona(string name);

    IReadOnlyList<string> ApprovedNames { get; }

    /// <summary>
    /// Pick a random enabled figure. When <paramref name="allowedGroupIds"/> is non-empty,
    /// restrict to figures in at least one of those groups (empty/null = any enabled figure);
    /// if that yields none, fall back to any enabled figure so the feed never dead-ends.
    /// Avoids <paramref name="exclude"/> unless doing so would leave nobody.
    /// </summary>
    string Pick(IReadOnlyCollection<string>? allowedGroupIds = null, string? exclude = null);
}

public sealed class RosterService : IRosterService
{
    private readonly IFigureStore _figures;
    public RosterService(IFigureStore figures) => _figures = figures;

    public bool IsApproved(string name) => _figures.Get(name) is not null;
    public string? Persona(string name) => _figures.Get(name)?.Persona;
    public IReadOnlyList<string> ApprovedNames => _figures.GetAll().Select(f => f.Name).ToList();

    public string Pick(IReadOnlyCollection<string>? allowedGroupIds = null, string? exclude = null)
    {
        var enabled = _figures.GetAll().Where(f => f.Enabled).ToList();
        if (enabled.Count == 0) return "";

        var pool = enabled;
        if (allowedGroupIds is { Count: > 0 })
        {
            var scoped = enabled.Where(f => f.GroupIds.Any(allowedGroupIds.Contains)).ToList();
            if (scoped.Count > 0) pool = scoped; // else keep all enabled (fallback)
        }

        if (exclude is not null)
        {
            var without = pool.Where(f => f.Name != exclude).ToList();
            if (without.Count > 0) pool = without;
        }

        return pool[Random.Shared.Next(pool.Count)].Name;
    }
}
