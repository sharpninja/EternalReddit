namespace EternalReddit.Shared.Models;

/// <summary>One captured log line, surfaced on the in-app /logs page.</summary>
public sealed record AppEvent(DateTime Utc, string Level, string Category, string Message);
