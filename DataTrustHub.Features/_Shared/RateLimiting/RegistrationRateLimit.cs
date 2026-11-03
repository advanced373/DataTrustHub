using System.Threading.RateLimiting;
using DataTrustHub.Features._Shared.Registration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DataTrustHub.Features._Shared.RateLimiting;

// The Registration policy (ADR-0026): a fixed window per client address, so one address cannot fill the
// queue of registration requests. The address is the browser's address sent by the WebApp with the shared key
// (TrustedClientAddress), or else the connection's: App Service passes the client's own address, and no
// forwarded headers are read
internal static class RegistrationRateLimit
{
    internal static readonly TimeSpan Window = TimeSpan.FromHours(1);

    // No queue: a request over the limit is rejected at once
    internal const int QueueLimit = 0;

    // A request without a client address shares one partition
    internal const string NoAddressPartitionKey = "no-address";

    // The limit is read when an address's limiter is first created (RegistrationOptions.PermitsPerHourPerIp)
    internal static RateLimitPartition<string> Partition(HttpContext httpContext)
    {
        var options = httpContext.RequestServices.GetRequiredService<IOptions<RegistrationOptions>>().Value;
        var clientAddress = TrustedClientAddress.Resolve(httpContext, options.TrustedClientKey);

        return RateLimitPartition.GetFixedWindowLimiter(
            clientAddress?.ToString() ?? NoAddressPartitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = options.PermitsPerHourPerIp,
                Window = Window,
                QueueLimit = QueueLimit
            });
    }
}
