using System.ComponentModel.DataAnnotations;

namespace DataTrustHub.Features._Shared.Registration;

// The limits on sign-ups (ADR-0026), read from configuration: Registration:PendingRequestLifetimeDays,
// Registration:PermitsPerHourPerIp, Registration:MaxPendingRequests. A value below 1, or a lifetime above
// MaxPendingRequestLifetimeDays, stops the host at startup
public class RegistrationOptions
{
    public const string SectionName = "Registration";

    // The defaults; appsettings.json states the same values
    public const int DefaultPendingRequestLifetimeDays = 14;
    public const int DefaultPermitsPerHourPerIp = 5;
    public const int DefaultMaxPendingRequests = 100;

    internal const int MinimumValue = 1;

    // Ten years: a longer lifetime would make the purge cutoff (now - lifetime) fall outside DateTime's range
    public const int MaxPendingRequestLifetimeDays = 3650;

    // A pending registration request older than this is purged (R2)
    [Range(MinimumValue, MaxPendingRequestLifetimeDays)]
    public int PendingRequestLifetimeDays { get; init; } = DefaultPendingRequestLifetimeDays;

    // Register calls one client address may make per hour (RateLimitPolicies.Registration)
    [Range(MinimumValue, int.MaxValue)]
    public int PermitsPerHourPerIp { get; init; } = DefaultPermitsPerHourPerIp;

    // At this many pending registration requests, register answers 503 until some are approved, rejected or expire (R3)
    [Range(MinimumValue, int.MaxValue)]
    public int MaxPendingRequests { get; init; } = DefaultMaxPendingRequests;

    public TimeSpan PendingRequestLifetime => TimeSpan.FromDays(PendingRequestLifetimeDays);
}
