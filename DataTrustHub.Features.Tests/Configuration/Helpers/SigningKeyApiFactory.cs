using DataTrustHub.Features.Tests.Helpers;
using Microsoft.AspNetCore.Hosting;

namespace DataTrustHub.Features.Tests.Configuration.Helpers;

/// <summary>
/// An API host with a chosen JWT signing key: null stands for a key that is not configured. The value wins
/// over the default test key, User Secrets and environment variables, so the outcome does not depend on
/// the machine.
/// </summary>
public sealed class SigningKeyApiFactory(string? signingKey) : ApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        TestJwt.SupplySigningKey(builder, signingKey);
    }
}
