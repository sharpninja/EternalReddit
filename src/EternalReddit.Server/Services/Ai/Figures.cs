namespace EternalReddit.Server.Services.Ai;

/// <summary>
/// The approved cast. Every AI comment/post speaks as one of these figures; the
/// server assigns the figure (the model never picks a name), so unapproved names
/// can't appear. Christopher Columbus is handled separately as the scripted gag.
/// Edit this list to change who can show up.
/// </summary>
public static class Figures
{
    public static readonly IReadOnlyList<string> Approved = new[]
    {
        "Isaac Newton", "Albert Einstein", "Nikola Tesla", "Marie Curie",
        "Charles Darwin", "Galileo Galilei", "Ada Lovelace", "Alan Turing",
        "Michael Faraday", "Louis Pasteur", "Rosalind Franklin", "Grace Hopper",
        "Katherine Johnson", "Hypatia", "Socrates", "Plato", "Aristotle",
        "Confucius", "Immanuel Kant", "Simone de Beauvoir", "Leonardo da Vinci",
        "William Shakespeare", "Jane Austen", "Mary Shelley", "Mark Twain",
        "Frida Kahlo", "Vincent van Gogh", "Amelia Earhart", "Benjamin Franklin",
        "Mary Somerville", "Émilie du Châtelet"
    };

    /// <summary>Pick a random approved figure, optionally avoiding one (e.g. the parent commenter).</summary>
    public static string Pick(string? exclude = null)
    {
        var pool = exclude is null ? Approved : Approved.Where(f => f != exclude).ToList();
        if (pool.Count == 0) pool = Approved;
        return pool[Random.Shared.Next(pool.Count)];
    }

    public static bool IsApproved(string figure) => Approved.Contains(figure);
}
