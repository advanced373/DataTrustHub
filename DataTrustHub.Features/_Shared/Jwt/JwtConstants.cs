namespace DataTrustHub.Features._Shared.Jwt;

internal static class JwtConstants
{
    // The configuration key of the JWT signing key; never committed (ADR-0007, ADR-0017)
    internal const string SecretKey = "Jwt:Secret";

    // HMAC-SHA256 needs a key of at least 256 bits
    internal const int MinimumSecretBytes = 32;

    internal const string SectionMissingMessage = "Jwt configuration section is missing.";

    // The messages name the setting and where to set it, never its value
    internal const string SecretMissingMessage =
        "The JWT signing key '" + SecretKey + "' is not configured. Set it in User Secrets "
        + "(dotnet user-secrets set \"" + SecretKey + "\" \"<random key>\" --project DataTrustHub.API, "
        + "see ADR-0007 and docs/development-setup.md) or in the environment variable Jwt__Secret.";

    internal static readonly string SecretTooShortMessage =
        $"The JWT signing key '{SecretKey}' is too short: HMAC-SHA256 needs at least {MinimumSecretBytes} "
        + "bytes in UTF-8. Set a longer random key in User Secrets (see ADR-0007 and docs/development-setup.md) "
        + "or in the environment variable Jwt__Secret.";
}
