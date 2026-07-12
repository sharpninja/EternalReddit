namespace EternalReddit.Shared.Models;

/// <summary>AI providers the server can call. Credentials come only from env vars.</summary>
public enum AiProvider
{
    HuggingFace,
    Claude,
    OpenAI,
    Grok
}

/// <summary>Direction of a vote; the value is the karma delta.</summary>
public enum VoteKind
{
    Up = 1,
    Down = -1
}

/// <summary>What the Moderator concluded about a piece of content.</summary>
public enum ModerationVerdict
{
    Clean,
    Nsfw,
    PromptInjection
}

/// <summary>What the system does in response to a verdict.</summary>
public enum ModerationAction
{
    Allow,
    Block,
    Ban
}

/// <summary>Whether a vote / moderation record targets a post or a comment.</summary>
public enum TargetKind
{
    Post,
    Reply
}
