using DataTrustHub.Features.Tests.Helpers;
using Microsoft.AspNetCore.Hosting;

namespace DataTrustHub.Features.Tests.Authentication.Helpers;

/// <summary>
/// The register host of <see cref="RegistrationApiFactory"/> with the trusted client key setting
/// (Registration:TrustedClientKey) and every log entry captured, for the trusted client address tests
/// (#172 PR 4). The connection address is still named per request with
/// <see cref="RegistrationApiFactory.ClientAddressHeader"/>.
/// </summary>
internal sealed class TrustedClientApiFactory(IDictionary<string, string?> settings) : RegistrationApiFactory(settings)
{
    internal const string TrustedClientKeySetting = "Registration:TrustedClientKey";

    // The header names of the contract (shared by name only between the WebApp and the API)
    internal const string TrustedAddressHeader = "X-DTH-Client-Address";
    internal const string TrustedKeyHeader = "X-DTH-Client-Key";

    internal CapturedLogs Logs { get; } = new();

    // A host with the given key (null: no key configured) and register limit
    internal static async Task<TrustedClientApiFactory> StartAsync(string? trustedClientKey, int permits)
    {
        var factory = new TrustedClientApiFactory(new Dictionary<string, string?>
        {
            // Empty, not missing, so a key from User Secrets on this machine cannot leak in
            [TrustedClientKeySetting] = trustedClientKey ?? string.Empty,
            [PermitsPerHourPerIpSetting] = permits.ToString()
        });
        await StartFactoryAsync(factory);
        return factory;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureLogging(Logs.AddTo);
    }
}
