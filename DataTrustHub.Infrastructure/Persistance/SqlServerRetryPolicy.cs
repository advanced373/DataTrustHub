namespace DataTrustHub.Infrastructure.Persistance;

// Retry settings for SQL Server transient failures, such as error 40613 while the free-offer serverless
// database resumes from auto-pause (up to about a minute, ADR-0021). With EF Core's exponential backoff,
// eight retries capped at 30 seconds each add about two minutes of backoff, which covers the resume.
// The worst case is longer: each attempt's SqlClient connect timeout comes on top of the backoff.
public static class SqlServerRetryPolicy
{
    public const int MaxRetryCount = 8;
    public const int MaxRetryDelaySeconds = 30;

    public static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(MaxRetryDelaySeconds);
}
