using EternalReddit.Server.Services;

namespace EternalReddit.Tests.Fakes;

public sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; private set; } = DateTimeOffset.UnixEpoch;
    public void Advance(TimeSpan by) => UtcNow += by;
}
