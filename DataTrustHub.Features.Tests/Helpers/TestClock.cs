namespace DataTrustHub.Features.Tests.Helpers;

// A TimeProvider the test moves by hand. The app resolves TimeProvider from DI, so a test host can replace it
public sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public TestClock() : this(DateTimeOffset.UtcNow)
    {
    }

    public override DateTimeOffset GetUtcNow() => _now;

    public DateTime UtcNow => _now.UtcDateTime;

    public void Advance(TimeSpan by) => _now += by;
}
