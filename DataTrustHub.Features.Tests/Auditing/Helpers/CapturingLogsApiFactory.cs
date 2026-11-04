using System.Collections.Concurrent;
using DataTrustHub.Features.Tests.Helpers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace DataTrustHub.Features.Tests.Auditing.Helpers;

// The API with an extra logger provider that keeps every log line the app writes at its configured levels,
// so a test can check that a value never reaches the logs
public class CapturingLogsApiFactory : ApiFactory
{
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
