using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace DataTrustHub.Features.Tests.Helpers;

/// <summary>
/// Keeps every log entry a host writes, at every level (Trace included, whatever the app's configured levels):
/// the formatted message, every structured value and the exception, so a test can check that a secret never
/// reaches the logs. Added with <see cref="AddTo"/>.
/// </summary>
internal sealed class CapturedLogs
{
    private readonly ConcurrentQueue<string> lines = new();

    internal IReadOnlyList<string> Lines => [.. lines];

    // Registers the capture as a logger provider that is never filtered out
    internal void AddTo(ILoggingBuilder logging)
    {
        logging.AddProvider(new Provider(lines));
        logging.AddFilter<Provider>(category: null, LogLevel.Trace);
        logging.SetMinimumLevel(LogLevel.Trace);
    }

    internal bool Contain(string text) => Lines.Any(line => line.Contains(text, StringComparison.Ordinal));

    private sealed class Provider(ConcurrentQueue<string> lines) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Logger(lines, categoryName);

        public void Dispose()
        {
        }
    }

    private sealed class Logger(ConcurrentQueue<string> lines, string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            lines.Enqueue($"{categoryName} scope: {Describe(state)}");
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            lines.Enqueue($"{categoryName} {logLevel}: {formatter(state, exception)} | {Describe(state)} | {exception}");

        private static string Describe<TState>(TState state) =>
            state is IEnumerable<KeyValuePair<string, object?>> values
                ? string.Join("; ", values.Select(value => $"{value.Key}={value.Value}"))
                : state?.ToString() ?? string.Empty;
    }
}
