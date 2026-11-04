namespace DataTrustHub.Features._Shared.RateLimiting;

// The names of the rate limit policies an endpoint can require with RequireRateLimiting (ADR-0025)
public static class RateLimitPolicies
{
    // Lookups that can confirm that a platform account exists (find a user by email)
    public const string UserLookup = "UserLookup";

    // Anonymous sign-ups, per client address (ADR-0026)
    public const string Registration = "Registration";
}
