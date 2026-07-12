namespace EternalReddit.Shared.Models;

/// <summary>
/// An approved character: the display name, the persona the AI writes in-voice, and
/// the peer groups it belongs to. A row existing means "approved" (its past comments
/// survive the startup purge); <see cref="Enabled"/> means "eligible for new AI picks",
/// so an admin can bench a figure without deleting its history.
/// </summary>
public sealed class Figure
{
    /// <summary>Display name; the id, and what <see cref="Reply.Figure"/> stores.</summary>
    public string Name { get; set; } = "";
    public string Persona { get; set; } = "";

    /// <summary>Peer groups this figure belongs to (may be several).</summary>
    public List<string> GroupIds { get; set; } = new();

    /// <summary>Pickable for new AI content.</summary>
    public bool Enabled { get; set; } = true;
}
