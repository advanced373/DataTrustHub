using System.ComponentModel.DataAnnotations;

namespace DataTrustHub.Features._Shared.Registration;

// The limits on sign-ups (ADR-0026), read from configuration: Registration:PendingRequestLifetimeDays,
// Registration:PermitsPerHourPerIp, Registration:MaxPendingRequests, and the secret Registration:TrustedClientKey.
// A value below 1, a lifetime above MaxPendingRequestLifetimeDays, or a set key shorter than
// MinimumTrustedClientKeyLength stops the host at startup
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

    // The shortest TrustedClientKey accepted when one is set; a shorter key stops the host at startup
    public const int MinimumTrustedClientKeyLength = 32;

    // The message names the setting and the rule, never the key
    public static readonly string TrustedClientKeyTooShortMessage =
        $"{SectionName}:{nameof(TrustedClientKey)} is too short: a set key needs at least "
        + $"{MinimumTrustedClientKeyLength} characters. Leave it empty to trust no client address header.";

    // The key the WebApp sends with the browser's address (RateLimiting.TrustedClientAddress). A secret: set in
    // User Secrets or the environment (Registration__TrustedClientKey), never in appsettings.json. Empty or
    // missing: no client address header is trusted
    public string? TrustedClientKey { get; init; }

    public TimeSpan PendingRequestLifetime => TimeSpan.FromDays(PendingRequestLifetimeDays);

    // An empty key is allowed (the headers are then never trusted); a set key must not be guessable
    internal bool HasAcceptableTrustedClientKey() =>
        string.IsNullOrWhiteSpace(TrustedClientKey) || TrustedClientKey.Length >= MinimumTrustedClientKeyLength;
}
