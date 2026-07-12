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
        // Artists & composers
        "William Shakespeare", "Leonardo da Vinci", "Wolfgang Amadeus Mozart",
        "Johann Sebastian Bach", "Ludwig van Beethoven",
        // Scientists & inventors
        "Isaac Newton", "Albert Einstein", "Nikola Tesla", "Alexander Graham Bell",
        "Erwin Schrödinger", "Benjamin Franklin",
        // Philosophers & antiquity
        "Socrates", "Plato", "Sun Tzu", "Julius Caesar", "Cleopatra",
        // Rulers, leaders & the frontier
        "Joan of Arc", "Elizabeth I", "Robert E. Lee", "Ulysses S. Grant",
        "Hiawatha", "Sam Houston", "Theodore Roosevelt", "Neville Chamberlain",
        // Generals
        "George S. Patton", "Bernard Montgomery", "Erwin Rommel", "Douglas MacArthur",
        // Writers
        "Geoffrey Chaucer", "Edgar Allan Poe", "Herman Melville", "Mark Twain",
        "Ernest Hemingway", "J.R.R. Tolkien",
        // Music
        "Elvis Presley",
        // Legendary / mythical (not historical people)
        "Beowulf", "King Arthur", "Lancelot", "Morgan le Fay", "Merlin",
        // 20th-century figures
        "Ronald Reagan", "Mahatma Gandhi", "John Wayne", "George Lucas",
        "Mick Jagger", "David Bowie"
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
