using System.Threading.RateLimiting;
using DataTrustHub.Features._Shared.Extensions;
using Microsoft.AspNetCore.Http;

namespace DataTrustHub.Features._Shared.RateLimiting;

// The UserLookup policy (ADR-0025): a fixed window per caller, so an admin of several organizations
// shares one budget across all of them
internal static class UserLookupRateLimit
{
    internal const int PermitLimit = 10;
    internal static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    // No queue: a request over the limit is rejected at once
    internal const int QueueLimit = 0;

    // A request without a caller id shares one partition; the endpoint requires authentication anyway
    internal const string NoCallerPartitionKey = "no-caller";

    internal static RateLimitPartition<string> Partition(HttpContext httpContext) =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.User.GetUserId()?.ToString() ?? NoCallerPartitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = PermitLimit,
                Window = Window,
                QueueLimit = QueueLimit
            });
}
