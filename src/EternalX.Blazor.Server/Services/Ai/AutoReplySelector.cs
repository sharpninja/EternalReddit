using EternalX.Blazor.Shared.Models;

namespace EternalX.Blazor.Server.Services.Ai;

/// <summary>
/// Chooses which thread the background service should nudge with a new AI reply:
/// one that was recently active but has gone quiet for a short while. Pure logic
/// so the background timer stays trivial and testable.
/// </summary>
public static class AutoReplySelector
{
    /// <param name="quietFor">Minimum idle time before we inject a reply.</param>
    /// <param name="activeWithin">Maximum age of last activity to still count as "alive".</param>
    public static Post? SelectThread(IEnumerable<Post> posts, DateTime now, TimeSpan quietFor, TimeSpan activeWithin)
    {
        Post? best = null;
        DateTime bestActivity = default;

        foreach (var post in posts)
        {
            var last = LastActivity(post);
            var idle = now - last;

            if (idle < quietFor) continue;      // too fresh - give humans/others a chance
            if (idle > activeWithin) continue;  // too stale - thread is dead

            if (best is null || last > bestActivity)
            {
                best = post;
                bestActivity = last;
            }
        }

        return best;
    }

    private static DateTime LastActivity(Post post)
    {
        var last = post.CreatedUtc;
        foreach (var reply in post.Replies)
            if (reply.CreatedUtc > last)
                last = reply.CreatedUtc;
        return last;
    }
}
