using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Registrations;
using DataTrustHub.Features.Tests.Infrastructure.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataTrustHub.Features.Tests.Infrastructure;

// #172 (spec: the IRegistrationRequestRepository port): the repository maps RegistrationRequest to its row and
// back, compares emails normalized (U7), counts the stored requests, purges the ones made before a time in one
// set-based delete that runs at once (outside the unit of work), and offers GetByIdAsync and RemoveAsync for
// approval and rejection. Each test has its own SQLite database (the purge needs a relational provider);
// each read uses a fresh scope, so the assertions see what was really saved
public sealed class RegistrationRequestRepositoryTests : IAsyncLifetime
{
    private const string Email = "repo.request@example.com";
    private const string VariantEmail = "  REPO.Request@Example.COM\t";
    private const string HashedPassword = "hashed-password";

    private static readonly DateTime RequestedAt = new(2026, 11, 3, 10, 30, 15, 123, DateTimeKind.Utc);
    private static readonly TimeSpan OneTick = TimeSpan.FromTicks(1);
    private static readonly TimeSpan OneDay = TimeSpan.FromDays(1);

    private readonly SqliteApiFactory _factory = new();

    Task IAsyncLifetime.InitializeAsync() => ((IAsyncLifetime)_factory).InitializeAsync();

    Task IAsyncLifetime.DisposeAsync() => ((IAsyncLifetime)_factory).DisposeAsync();

    // --- Add and GetByIdAsync ---

    [Fact]
    public async Task Add_ThenSave_RoundTripsEveryField()
    {
        var request = RegistrationRequest.Submit(VariantEmail, HashedPassword, RequestedAt);
        await SaveAsync(request);

        var loaded = await QueryAsync(repo => repo.GetByIdAsync(request.Id, default));

        Assert.Equal((request.Id, Email, HashedPassword, RequestedAt),
            (loaded!.Id, loaded.Email, loaded.HashedPassword, loaded.RequestedAt));
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsTheTimeAsUtc()
    {
        var request = await SaveNewAsync(Email, RequestedAt);

        var loaded = await QueryAsync(repo => repo.GetByIdAsync(request.Id, default));

        Assert.Equal(DateTimeKind.Utc, loaded!.RequestedAt.Kind);
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        await SaveNewAsync(Email, RequestedAt);

        Assert.Null(await QueryAsync(repo => repo.GetByIdAsync(Guid.NewGuid(), default)));
    }

    [Fact]
    public async Task Add_WithoutSave_StoresNothing()
    {
        using (var scope = _factory.Services.CreateScope())
            scope.ServiceProvider.GetRequiredService<IRegistrationRequestRepository>()
                .Add(RegistrationRequest.Submit(Email, HashedPassword, RequestedAt));

        Assert.Equal(0, await QueryAsync(repo => repo.CountAsync(default)));
    }

    // --- EmailExistsAsync ---

    [Fact]
    public async Task EmailExistsAsync_CaseOrSpaceVariantOfAStoredEmail_IsTrue()
    {
        await SaveNewAsync(Email, RequestedAt);

        Assert.True(await QueryAsync(repo => repo.EmailExistsAsync(VariantEmail, default)));
    }

    [Fact]
    public async Task EmailExistsAsync_OtherEmail_IsFalse()
    {
        await SaveNewAsync(Email, RequestedAt);

        Assert.False(await QueryAsync(repo => repo.EmailExistsAsync("other.request@example.com", default)));
    }

    // --- CountAsync ---

    [Fact]
    public async Task CountAsync_CountsEveryStoredRequest()
    {
        await SaveNewAsync("one@example.com", RequestedAt);
        await SaveNewAsync("two@example.com", RequestedAt - OneDay);
        await SaveNewAsync("three@example.com", RequestedAt + OneDay);

        Assert.Equal(3, await QueryAsync(repo => repo.CountAsync(default)));
    }

    // --- RemoveExpiredAsync ---

    [Fact]
    public async Task RemoveExpiredAsync_DeletesOnlyTheRequestsMadeBeforeTheTime()
    {
        var older = await SaveNewAsync("older@example.com", RequestedAt - OneTick);
        var atTheTime = await SaveNewAsync("at.the.time@example.com", RequestedAt);
        var newer = await SaveNewAsync("newer@example.com", RequestedAt + OneDay);

        await QueryAsync(repo => repo.RemoveExpiredAsync(RequestedAt, default));

        Assert.Null(await QueryAsync(repo => repo.GetByIdAsync(older.Id, default)));
        Assert.NotNull(await QueryAsync(repo => repo.GetByIdAsync(atTheTime.Id, default)));
        Assert.NotNull(await QueryAsync(repo => repo.GetByIdAsync(newer.Id, default)));
    }

    [Fact]
    public async Task RemoveExpiredAsync_ReturnsHowManyItDeleted()
    {
        await SaveNewAsync("old1@example.com", RequestedAt - OneDay);
        await SaveNewAsync("old2@example.com", RequestedAt - OneDay - OneDay);
        await SaveNewAsync("kept@example.com", RequestedAt);

        Assert.Equal(2, await QueryAsync(repo => repo.RemoveExpiredAsync(RequestedAt, default)));
    }

    [Fact]
    public async Task RemoveExpiredAsync_NothingExpired_ReturnsZero()
    {
        await SaveNewAsync(Email, RequestedAt);

        Assert.Equal(0, await QueryAsync(repo => repo.RemoveExpiredAsync(RequestedAt - OneDay, default)));
    }

    // It runs at once: no SaveChangesAsync is needed for the rows to be gone
    [Fact]
    public async Task RemoveExpiredAsync_DeletesWithoutTheUnitOfWork()
    {
        await SaveNewAsync(Email, RequestedAt - OneDay);

        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IRegistrationRequestRepository>().RemoveExpiredAsync(RequestedAt, default);

        Assert.Equal(0, await QueryAsync(repo => repo.CountAsync(default)));
    }

    // It is its own statement: a request staged in the same scope is not saved by the purge
    [Fact]
    public async Task RemoveExpiredAsync_DoesNotCommitStagedChanges()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IRegistrationRequestRepository>();
            repo.Add(RegistrationRequest.Submit(Email, HashedPassword, RequestedAt));
            await repo.RemoveExpiredAsync(RequestedAt - OneDay, default);
        }

        Assert.Equal(0, await QueryAsync(repo => repo.CountAsync(default)));
    }

    // After the purge the email can be stored again (the unique index no longer holds it)
    [Fact]
    public async Task RemoveExpiredAsync_FreesTheEmailForANewRequest()
    {
        await SaveNewAsync(Email, RequestedAt - OneDay);
        await QueryAsync(repo => repo.RemoveExpiredAsync(RequestedAt, default));

        var again = await SaveNewAsync(VariantEmail, RequestedAt);

        Assert.Equal(again.Id, (await QueryAsync(repo => repo.GetByIdAsync(again.Id, default)))!.Id);
    }

    // --- RemoveAsync ---

    [Fact]
    public async Task RemoveAsync_ThenSave_DeletesTheRequest()
    {
        var request = await SaveNewAsync(Email, RequestedAt);
        var other = await SaveNewAsync("other.request@example.com", RequestedAt);

        await InScopeAsync(async (repo, unitOfWork) =>
        {
            await repo.RemoveAsync((await repo.GetByIdAsync(request.Id, default))!, default);
            await unitOfWork.SaveChangesAsync(default);
        });

        Assert.Null(await QueryAsync(repo => repo.GetByIdAsync(request.Id, default)));
        Assert.NotNull(await QueryAsync(repo => repo.GetByIdAsync(other.Id, default)));
    }

    [Fact]
    public async Task RemoveAsync_WithoutSave_DeletesNothing()
    {
        var request = await SaveNewAsync(Email, RequestedAt);

        await InScopeAsync(async (repo, _) => await repo.RemoveAsync(request, default));

        Assert.NotNull(await QueryAsync(repo => repo.GetByIdAsync(request.Id, default)));
    }

    // --- R1 in the database ---

    [Fact]
    public async Task Save_SecondRequestForTheSameNormalizedEmail_IsRejected()
    {
        await SaveNewAsync(Email, RequestedAt);

        var exception = await Record.ExceptionAsync(() => SaveNewAsync(VariantEmail, RequestedAt));

        Assert.NotNull(exception);
        Assert.Equal(1, await QueryAsync(repo => repo.CountAsync(default)));
    }

    // --- helpers ---

    private async Task<RegistrationRequest> SaveNewAsync(string email, DateTime requestedAt)
    {
        var request = RegistrationRequest.Submit(email, HashedPassword, requestedAt);
        await SaveAsync(request);
        return request;
    }

    private Task SaveAsync(RegistrationRequest request) =>
        InScopeAsync(async (repo, unitOfWork) =>
        {
            repo.Add(request);
            await unitOfWork.SaveChangesAsync(default);
        });

    private async Task InScopeAsync(Func<IRegistrationRequestRepository, IUnitOfWork, Task> action)
    {
        using var scope = _factory.Services.CreateScope();
        await action(
            scope.ServiceProvider.GetRequiredService<IRegistrationRequestRepository>(),
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>());
    }

    private async Task<T> QueryAsync<T>(Func<IRegistrationRequestRepository, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<IRegistrationRequestRepository>());
    }
}
