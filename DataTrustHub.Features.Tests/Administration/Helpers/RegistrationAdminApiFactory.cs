using System.Collections.Concurrent;
using DataTrustHub.Features.Tests.Authentication.Helpers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace DataTrustHub.Features.Tests.Administration.Helpers;

/// <summary>
/// The registration host on SQLite (the list route purges with a set-based delete that EF InMemory cannot run)
/// for the platform admin's registration request routes (#172 API 2). The pending lifetime is fixed, the clock
/// is the frozen <see cref="Helpers.TestClock"/> of <see cref="RegistrationApiFactory"/>, and every log line
/// the app writes is kept, so a test can check that no email or hash reaches the logs.
/// </summary>
public class RegistrationAdminApiFactory : RegistrationApiFactory
{
    internal const int LifetimeDays = 2;

    internal static readonly TimeSpan Lifetime = TimeSpan.FromDays(LifetimeDays);

    public RegistrationAdminApiFactory()
        : base(new Dictionary<string, string?> { [LifetimeDaysSetting] = LifetimeDays.ToString() })
    {
    }

    internal ConcurrentQueue<string> LogLines { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureLogging(logging => logging.AddProvider(new QueueLoggerProvider(LogLines)));
    }

    private sealed class QueueLoggerProvider(ConcurrentQueue<string> lines) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new QueueLogger(lines);

        public void Dispose()
        {
        }
    }

    private sealed class QueueLogger(ConcurrentQueue<string> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            lines.Enqueue($"{formatter(state, exception)} {exception}");
    }
}
