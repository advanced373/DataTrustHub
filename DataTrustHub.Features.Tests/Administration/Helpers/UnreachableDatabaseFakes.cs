using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Users;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace DataTrustHub.Features.Tests.Administration.Helpers;

// Fakes of a database that stays unreachable after the SQL Server retries (#146, ADR-0021): EF Core then
// throws RetryLimitExceededException, simulated here without a server and without waiting
internal static class UnreachableDatabase
{
    internal const string FailureMessage = "Maximum number of retries exceeded (simulated paused database).";

    internal static RetryLimitExceededException Failure() =>
        new(FailureMessage, new TimeoutException(FailureMessage));
}

// Unreachable for the first reads, then serves the wrapped repository: the database has resumed
internal sealed class ResumingUserRepository(FakeUserRepository resumed, int unreachableReads) : IUserRepository
{
    private int readsLeftUnreachable = unreachableReads;

    public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken)
    {
        if (readsLeftUnreachable-- > 0) throw UnreachableDatabase.Failure();
        return resumed.GetByEmailAsync(email, cancellationToken);
    }

    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        resumed.GetByIdAsync(id, cancellationToken);

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) =>
        resumed.ExistsAsync(id, cancellationToken);

    public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken) =>
        resumed.EmailExistsAsync(email, cancellationToken);

    public void Add(User user) => resumed.Add(user);

    public Task UpdateAsync(User user, CancellationToken cancellationToken) =>
        resumed.UpdateAsync(user, cancellationToken);

    public Task RemoveAsync(User user, CancellationToken cancellationToken) =>
        resumed.RemoveAsync(user, cancellationToken);
}

internal sealed class UnreachableUnitOfWork : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        throw UnreachableDatabase.Failure();
}

// Records every log entry with its rendered message, so a test can check what was logged and at which level
internal sealed class CapturingLogger<T> : ILogger<T>
{
    internal List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        Entries.Add((logLevel, formatter(state, exception), exception));
}
