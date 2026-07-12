using EternalX.Blazor.Server.Services;

namespace EternalX.Blazor.Tests.Fakes;

public sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; private set; } = DateTimeOffset.UnixEpoch;
    public void Advance(TimeSpan by) => UtcNow += by;
}
