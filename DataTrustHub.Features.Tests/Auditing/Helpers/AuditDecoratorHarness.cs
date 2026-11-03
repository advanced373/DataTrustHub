using System.Security.Claims;
using DataTrustHub.Domain.Auditing;
using DataTrustHub.Features._Shared.Auditing;
using DataTrustHub.Features._Shared.Extensions;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace DataTrustHub.Features.Tests.Auditing.Helpers;

// Builds the internal AuditCommandDecorator or AuditQueryDecorator around a fake inner handler, with a fake audit log, a request
// audit scope, the request's user and a logger that keeps what it logs
internal sealed class AuditDecoratorHarness
{
    private const string DecoratorTypeName = "DataTrustHub.Features._Shared.Auditing.AuditCommandDecorator`2";
    private const string QueryDecoratorTypeName = "DataTrustHub.Features._Shared.Auditing.AuditQueryDecorator`2";
    private const string AuthenticationType = "Test";

    internal FakeAuditLog AuditLog { get; } = new();
    internal AuditScope AuditScope { get; } = new();
    internal CapturingLoggerProvider Logs { get; } = new();
    internal HttpContextAccessor HttpContextAccessor { get; } = new();

    internal static Type DecoratorDefinition { get; } =
        typeof(FeaturesRegistration).Assembly.GetType(DecoratorTypeName, throwOnError: true)!;

    internal static Type QueryDecoratorDefinition { get; } =
        typeof(FeaturesRegistration).Assembly.GetType(QueryDecoratorTypeName, throwOnError: true)!;

    // An authenticated request whose user has these claims
    internal void SignIn(params Claim[] claims) =>
        HttpContextAccessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationType))
        };

    // A request without an authenticated user (register, log in)
    internal void SignInAnonymous(params Claim[] claims) =>
        HttpContextAccessor.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims)) };

    internal ICommandHandler<TCommand, TResult> Decorate<TCommand, TResult>(Func<TCommand, Task<TResult>> inner)
        where TResult : Result
    {
        var decoratorType = DecoratorDefinition.MakeGenericType(typeof(TCommand), typeof(TResult));
        var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(Logs));
        var logger = Activator.CreateInstance(typeof(Logger<>).MakeGenericType(decoratorType), loggerFactory)!;

        return (ICommandHandler<TCommand, TResult>)Activator.CreateInstance(
            decoratorType, new FakeHandler<TCommand, TResult>(inner), AuditLog, AuditScope, HttpContextAccessor, logger)!;
    }

    internal IQueryHandler<TQuery, TResult> DecorateQuery<TQuery, TResult>(Func<TQuery, Task<TResult>> inner)
        where TResult : Result
    {
        var decoratorType = QueryDecoratorDefinition.MakeGenericType(typeof(TQuery), typeof(TResult));
        var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(Logs));
        var logger = Activator.CreateInstance(typeof(Logger<>).MakeGenericType(decoratorType), loggerFactory)!;

        return (IQueryHandler<TQuery, TResult>)Activator.CreateInstance(
            decoratorType, new FakeQueryHandler<TQuery, TResult>(inner), AuditLog, AuditScope, HttpContextAccessor, logger)!;
    }

    private sealed class FakeQueryHandler<TQuery, TResult>(Func<TQuery, Task<TResult>> handle) : IQueryHandler<TQuery, TResult>
    {
        public Task<TResult> Handle(TQuery query, CancellationToken cancellationToken) => handle(query);
    }

    private sealed class FakeHandler<TCommand, TResult>(Func<TCommand, Task<TResult>> handle) : ICommandHandler<TCommand, TResult>
    {
        public Task<TResult> Handle(TCommand command, CancellationToken cancellationToken) => handle(command);
    }
}

// Keeps every appended record; throws instead when told to, like a database that is down
internal sealed class FakeAuditLog : IAuditLog
{
    internal List<AuditRecord> Appended { get; } = [];
    internal Exception? ThrowOnAppend { get; set; }
    internal List<CancellationToken> Tokens { get; } = [];

    public Task AppendAsync(AuditRecord record, CancellationToken cancellationToken)
    {
        Tokens.Add(cancellationToken);
        if (ThrowOnAppend is not null) throw ThrowOnAppend;
        Appended.Add(record);
        return Task.CompletedTask;
    }
}

internal sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    internal List<LogEntry> Entries { get; } = [];

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(Entries);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(List<LogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
    }
}
