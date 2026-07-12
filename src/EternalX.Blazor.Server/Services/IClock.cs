namespace EternalX.Blazor.Server.Services;

/// <summary>Abstraction over the system clock so time-based logic is testable.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
