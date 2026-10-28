using Microsoft.AspNetCore.Hosting;

namespace DataTrustHub.Features.Tests.Helpers;

/// <summary>
/// The JWT signing key of the test host. appsettings.json holds no signing key (ADR-0007), so every test
/// host supplies its own, independent of the developer's User Secrets and the machine's environment.
/// </summary>
internal static class TestJwt
{
    internal const string SecretKey = "Jwt:Secret";
    internal const string Issuer = "DataTrustHub";
    internal const string Audience = "DataTrustHub";

    // HMAC-SHA256 needs at least 32 bytes; this key exists only in tests and signs nothing real
    internal const int MinimumSecretBytes = 32;
    internal const string SigningKey = "test-only-jwt-signing-key-0123456789-abcdefghij";

    // UseSetting, not ConfigureAppConfiguration: Program reads the key while it registers the services,
    // before WebApplicationFactory applies ConfigureAppConfiguration callbacks
    internal static void SupplySigningKey(IWebHostBuilder builder, string? signingKey = SigningKey) =>
        builder.UseSetting(SecretKey, signingKey);
}
