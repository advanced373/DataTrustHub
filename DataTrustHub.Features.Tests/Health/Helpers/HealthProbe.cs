namespace DataTrustHub.Features.Tests.Health.Helpers;

// The liveness route both apps answer without touching the database or the API (#146, ADR-0021)
internal static class HealthProbe
{
    internal const string Route = "/health";
    internal const string HealthyBody = "Healthy";
}

// Counts how often something was reached that the health endpoint must never reach (DContext, the API)
internal sealed class TouchCounter
{
    private int count;

    internal int Count => Volatile.Read(ref count);

    internal void Touch() => Interlocked.Increment(ref count);
}
