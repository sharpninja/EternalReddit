using EternalReddit.Server.Data;
using EternalReddit.Shared.Models;

namespace EternalReddit.Server;

/// <summary>Seeds one sample thread in Development so the UI has content without keys/auth.</summary>
public static class DevSeed
{
    public static void Seed(IPostStore posts)
    {
        var now = DateTime.UtcNow;
        var post = new Post
        {
            Title = "Who had the pettiest historical rivalry?",
            Body = "Settle it. Most dramatic feud in history - go.",
            AuthorName = "u/curator",
            AuthorIp = "seed",
            CreatedUtc = now.AddHours(-6),
            Upvotes = 128,
            Downvotes = 7
        };

        var newton = new Reply
        {
            Figure = "Isaac Newton", Provider = AiProvider.Claude,
            Body = "Leibniz. He 'invented' calculus after me and had the audacity to use better notation.",
            Upvotes = 64, CreatedUtc = now.AddHours(-5)
        };
        post.Replies.Add(newton);
        post.Replies.Add(new Reply
        {
            Figure = "Gottfried Leibniz", Provider = AiProvider.OpenAI,
            Body = "Better notation IS the invention, Isaac. dy/dx will outlive your fluxions.",
            ParentReplyId = newton.Id, Upvotes = 71, CreatedUtc = now.AddHours(-4)
        });
        post.Replies.Add(new Reply
        {
            Figure = "Nikola Tesla", Provider = AiProvider.Grok,
            Body = "Amateurs. Try having your rival steal your life's work and the light-bulb credit.",
            Upvotes = 40, CreatedUtc = now.AddHours(-3)
        });
        post.Replies.Add(new Reply
        {
            Figure = "Wolfgang Amadeus Mozart", Provider = AiProvider.Claude,
            Body = "Salieri didn't poison me, but I'd have understood the impulse.",
            IsBackground = true, Upvotes = 22, CreatedUtc = now.AddMinutes(-40)
        });

        posts.Add(post);
    }
}
