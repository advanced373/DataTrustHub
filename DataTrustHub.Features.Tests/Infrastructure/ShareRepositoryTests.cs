using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.DataItems;
using DataTrustHub.Domain.Shares;
using DataTrustHub.Features.Tests.Infrastructure.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataTrustHub.Features.Tests.Infrastructure;

// #3 (ADR-0015, spec: the IShareRepository port and its adapter): the repository is registered in DI, maps Share to
// its row and back with every time as UTC, finds an active share in the database by the same rule as
// Share.IsActiveAt (S5), revokes only the not yet revoked shares of one data item, removes the shares a user
// received or gave, and never saves on its own: every write waits for IUnitOfWork. Each test has its own SQLite
// database (relational, so the foreign key to the data item holds); each read uses a fresh scope
public sealed class ShareRepositoryTests : IAsyncLifetime
{
    private const string ItemName = "shared.txt";
    private const string ItemContent = "YWJj";
    private const long ItemSize = 3;

    private static readonly DateTime SharedAt = new(2026, 11, 11, 10, 30, 15, 123, DateTimeKind.Utc);
    private static readonly TimeSpan OneTick = TimeSpan.FromTicks(1);
    private static readonly TimeSpan OneDay = TimeSpan.FromDays(1);
    private static readonly DateTime ExpiresAt = SharedAt + OneDay;
    private static readonly DateTime RevokedAt = SharedAt + OneDay / 2;
    // Later than RevokedAt and earlier than ExpiresAt
    private static readonly DateTime Now = SharedAt + OneDay * 3 / 4;

    private readonly SqliteApiFactory _factory = new();
    private readonly Guid _ownerUserId = Guid.NewGuid();
    private readonly Guid _recipientUserId = Guid.NewGuid();

    public enum ShareState
    {
        NoExpiry,
        NotYetExpired,
        ExpiresExactlyNow,
        Expired,
        Revoked,
        RevokedWithFutureExpiry
    }

    public static TheoryData<ShareState, bool> ActiveCases => new()
    {
        { ShareState.NoExpiry, true },
        { ShareState.NotYetExpired, true },
        { ShareState.ExpiresExactlyNow, false },
        { ShareState.Expired, false },
        { ShareState.Revoked, false },
        { ShareState.RevokedWithFutureExpiry, false }
    };

    Task IAsyncLifetime.InitializeAsync() => ((IAsyncLifetime)_factory).InitializeAsync();

    Task IAsyncLifetime.DisposeAsync() => ((IAsyncLifetime)_factory).DisposeAsync();

    // --- registration ---

    [Fact]
    public void Repository_IsRegisteredInDependencyInjection()
    {
        using var scope = _factory.Services.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetService<IShareRepository>());
    }

    // --- Add and GetByIdAsync ---

    [Fact]
    public async Task Add_ThenSave_RoundTripsEveryField()
    {
        var itemId = await SaveDataItemAsync();
        var share = NewShare(itemId, _recipientUserId, ExpiresAt);
        await SaveAsync(share);

        var loaded = (await QueryAsync(repo => repo.GetByIdAsync(share.Id, default)))!;

        Assert.Equal(
            (share.Id, itemId, _recipientUserId, _ownerUserId, SharedAt, (DateTime?)ExpiresAt, (DateTime?)null),
            (loaded.Id, loaded.DataItemId, loaded.RecipientUserId, loaded.SharedByUserId, loaded.SharedAt, loaded.ExpiresAt, loaded.RevokedAt));
    }

    [Fact]
    public async Task Add_WithoutExpiry_RoundTripsANullExpiry()
    {
        var share = NewShare(await SaveDataItemAsync(), _recipientUserId, null);
        await SaveAsync(share);

        Assert.Null((await QueryAsync(repo => repo.GetByIdAsync(share.Id, default)))!.ExpiresAt);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsEveryTimeAsUtc()
    {
        var share = NewShare(await SaveDataItemAsync(), _recipientUserId, ExpiresAt);
        await SaveAsync(share);
        await RevokeAsync(share.Id, RevokedAt);

        var loaded = (await QueryAsync(repo => repo.GetByIdAsync(share.Id, default)))!;

        Assert.Equal(
            (DateTimeKind.Utc, DateTimeKind.Utc, DateTimeKind.Utc),
            (loaded.SharedAt.Kind, loaded.ExpiresAt!.Value.Kind, loaded.RevokedAt!.Value.Kind));
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        await SaveAsync(NewShare(await SaveDataItemAsync(), _recipientUserId, null));

        Assert.Null(await QueryAsync(repo => repo.GetByIdAsync(Guid.NewGuid(), default)));
    }

    [Fact]
    public async Task Add_WithoutSave_StoresNothing()
    {
        var share = NewShare(await SaveDataItemAsync(), _recipientUserId, null);

        using (var scope = _factory.Services.CreateScope())
            scope.ServiceProvider.GetRequiredService<IShareRepository>().Add(share);

        Assert.Null(await QueryAsync(repo => repo.GetByIdAsync(share.Id, default)));
    }

    // --- UpdateAsync ---

    [Fact]
    public async Task UpdateAsync_ThenSave_PersistsTheRevocation()
    {
        var share = NewShare(await SaveDataItemAsync(), _recipientUserId, ExpiresAt);
        await SaveAsync(share);

        await RevokeAsync(share.Id, RevokedAt);

        var loaded = (await QueryAsync(repo => repo.GetByIdAsync(share.Id, default)))!;
        Assert.Equal(
            (share.Id, share.DataItemId, (DateTime?)ExpiresAt, (DateTime?)RevokedAt),
            (loaded.Id, loaded.DataItemId, loaded.ExpiresAt, loaded.RevokedAt));
    }

    [Fact]
    public async Task UpdateAsync_WithoutSave_ChangesNothing()
    {
        var share = NewShare(await SaveDataItemAsync(), _recipientUserId, null);
        await SaveAsync(share);

        await InScopeAsync(async (repo, _) =>
        {
            var stored = (await repo.GetByIdAsync(share.Id, default))!;
            stored.Revoke(RevokedAt);
            await repo.UpdateAsync(stored, default);
        });

        Assert.Null((await QueryAsync(repo => repo.GetByIdAsync(share.Id, default)))!.RevokedAt);
    }

    // --- HasActiveAsync (S5 in the database) ---

    [Theory]
    [MemberData(nameof(ActiveCases))]
    public async Task HasActiveAsync_AgreesWithIsActiveAt(ShareState state, bool expected)
    {
        var itemId = await SaveDataItemAsync();
        var share = await SaveInStateAsync(itemId, state);

        var active = await QueryAsync(repo => repo.HasActiveAsync(itemId, _recipientUserId, Now, default));

        Assert.Equal((expected, expected), (active, share.IsActiveAt(Now)));
    }

    [Fact]
    public async Task HasActiveAsync_ActiveShareOfAnotherRecipient_IsFalse()
    {
        var itemId = await SaveDataItemAsync();
        await SaveAsync(NewShare(itemId, Guid.NewGuid(), null));

        Assert.False(await QueryAsync(repo => repo.HasActiveAsync(itemId, _recipientUserId, Now, default)));
    }

    [Fact]
    public async Task HasActiveAsync_ActiveShareOfAnotherDataItem_IsFalse()
    {
        var itemId = await SaveDataItemAsync();
        await SaveAsync(NewShare(await SaveDataItemAsync(), _recipientUserId, null));

        Assert.False(await QueryAsync(repo => repo.HasActiveAsync(itemId, _recipientUserId, Now, default)));
    }

    [Fact]
    public async Task HasActiveAsync_NoShare_IsFalse()
    {
        var itemId = await SaveDataItemAsync();

        Assert.False(await QueryAsync(repo => repo.HasActiveAsync(itemId, _recipientUserId, Now, default)));
    }

    [Fact]
    public async Task HasActiveAsync_OneEndedAndOneActiveShare_IsTrue()
    {
        var itemId = await SaveDataItemAsync();
        await SaveInStateAsync(itemId, ShareState.Revoked);
        await SaveInStateAsync(itemId, ShareState.NoExpiry);

        Assert.True(await QueryAsync(repo => repo.HasActiveAsync(itemId, _recipientUserId, Now, default)));
    }

    // --- RevokeAllForDataItemAsync ---

    [Fact]
    public async Task RevokeAllForDataItemAsync_ThenSave_RevokesEveryActiveShareOfTheItem()
    {
        var itemId = await SaveDataItemAsync();
        var first = NewShare(itemId, _recipientUserId, null);
        var second = NewShare(itemId, Guid.NewGuid(), ExpiresAt);
        await SaveAsync(first);
        await SaveAsync(second);

        await RevokeAllAsync(itemId, RevokedAt);

        Assert.Equal((DateTime?)RevokedAt, (await QueryAsync(repo => repo.GetByIdAsync(first.Id, default)))!.RevokedAt);
        Assert.Equal((DateTime?)RevokedAt, (await QueryAsync(repo => repo.GetByIdAsync(second.Id, default)))!.RevokedAt);
    }

    [Fact]
    public async Task RevokeAllForDataItemAsync_AlreadyRevokedShare_KeepsItsRevocationTime()
    {
        var itemId = await SaveDataItemAsync();
        var revoked = NewShare(itemId, _recipientUserId, null);
        await SaveAsync(revoked);
        await RevokeAsync(revoked.Id, RevokedAt);

        await RevokeAllAsync(itemId, RevokedAt + OneDay);

        Assert.Equal((DateTime?)RevokedAt, (await QueryAsync(repo => repo.GetByIdAsync(revoked.Id, default)))!.RevokedAt);
    }

    [Fact]
    public async Task RevokeAllForDataItemAsync_ExpiredShare_IsRevoked()
    {
        var itemId = await SaveDataItemAsync();
        var expired = NewShare(itemId, _recipientUserId, ExpiresAt);
        await SaveAsync(expired);
        var revokedAfterExpiry = ExpiresAt + OneDay;

        await RevokeAllAsync(itemId, revokedAfterExpiry);

        Assert.Equal((DateTime?)revokedAfterExpiry, (await QueryAsync(repo => repo.GetByIdAsync(expired.Id, default)))!.RevokedAt);
    }

    [Fact]
    public async Task RevokeAllForDataItemAsync_LeavesOtherItemsSharesAlone()
    {
        var itemId = await SaveDataItemAsync();
        var otherItemShare = NewShare(await SaveDataItemAsync(), _recipientUserId, null);
        await SaveAsync(NewShare(itemId, _recipientUserId, null));
        await SaveAsync(otherItemShare);

        await RevokeAllAsync(itemId, RevokedAt);

        Assert.Null((await QueryAsync(repo => repo.GetByIdAsync(otherItemShare.Id, default)))!.RevokedAt);
    }

    [Fact]
    public async Task RevokeAllForDataItemAsync_WithoutSave_RevokesNothing()
    {
        var itemId = await SaveDataItemAsync();
        var share = NewShare(itemId, _recipientUserId, null);
        await SaveAsync(share);

        await InScopeAsync((repo, _) => repo.RevokeAllForDataItemAsync(itemId, RevokedAt, default));

        Assert.Null((await QueryAsync(repo => repo.GetByIdAsync(share.Id, default)))!.RevokedAt);
    }

    // --- RemoveAllForUserAsync ---

    [Fact]
    public async Task RemoveAllForUserAsync_ThenSave_RemovesTheSharesTheUserReceived()
    {
        var received = NewShare(await SaveDataItemAsync(), _recipientUserId, null);
        await SaveAsync(received);

        await RemoveAllForUserAsync(_recipientUserId);

        Assert.Null(await QueryAsync(repo => repo.GetByIdAsync(received.Id, default)));
    }

    [Fact]
    public async Task RemoveAllForUserAsync_ThenSave_RemovesTheSharesTheUserGave()
    {
        var given = NewShare(await SaveDataItemAsync(), _recipientUserId, null);
        await SaveAsync(given);

        await RemoveAllForUserAsync(_ownerUserId);

        Assert.Null(await QueryAsync(repo => repo.GetByIdAsync(given.Id, default)));
    }

    [Fact]
    public async Task RemoveAllForUserAsync_RemovesRevokedSharesToo()
    {
        var revoked = NewShare(await SaveDataItemAsync(), _recipientUserId, null);
        await SaveAsync(revoked);
        await RevokeAsync(revoked.Id, RevokedAt);

        await RemoveAllForUserAsync(_recipientUserId);

        Assert.Null(await QueryAsync(repo => repo.GetByIdAsync(revoked.Id, default)));
    }

    [Fact]
    public async Task RemoveAllForUserAsync_LeavesOtherUsersSharesAlone()
    {
        var itemId = await SaveDataItemAsync();
        var otherOwner = Guid.NewGuid();
        var unrelated = Share.Create(itemId, Guid.NewGuid(), otherOwner, SharedAt, null).Value;
        await SaveAsync(NewShare(itemId, _recipientUserId, null));
        await SaveAsync(unrelated);

        await RemoveAllForUserAsync(_recipientUserId);

        Assert.NotNull(await QueryAsync(repo => repo.GetByIdAsync(unrelated.Id, default)));
    }

    [Fact]
    public async Task RemoveAllForUserAsync_WithoutSave_RemovesNothing()
    {
        var share = NewShare(await SaveDataItemAsync(), _recipientUserId, null);
        await SaveAsync(share);

        await InScopeAsync((repo, _) => repo.RemoveAllForUserAsync(_recipientUserId, default));

        Assert.NotNull(await QueryAsync(repo => repo.GetByIdAsync(share.Id, default)));
    }

    // --- helpers ---

    private Share NewShare(Guid dataItemId, Guid recipientUserId, DateTime? expiresAt) =>
        Share.Create(dataItemId, recipientUserId, _ownerUserId, SharedAt, expiresAt).Value;

    private async Task<Share> SaveInStateAsync(Guid itemId, ShareState state)
    {
        var expiresAt = state switch
        {
            ShareState.NotYetExpired or ShareState.RevokedWithFutureExpiry => Now + OneTick,
            ShareState.ExpiresExactlyNow => Now,
            ShareState.Expired => Now - OneTick,
            _ => (DateTime?)null
        };
        var share = NewShare(itemId, _recipientUserId, expiresAt);
        await SaveAsync(share);
        if (state is ShareState.Revoked or ShareState.RevokedWithFutureExpiry)
        {
            share.Revoke(RevokedAt);
            await RevokeAsync(share.Id, RevokedAt);
        }
        return share;
    }

    private async Task<Guid> SaveDataItemAsync()
    {
        var item = DataItem.CreatePersonal(ItemName, ItemSize, ItemContent, _ownerUserId);
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IDataItemRepository>().Add(item);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(default);
        return item.Id;
    }

    private Task SaveAsync(Share share) =>
        InScopeAsync(async (repo, unitOfWork) =>
        {
            repo.Add(share);
            await unitOfWork.SaveChangesAsync(default);
        });

    private Task RevokeAsync(Guid shareId, DateTime revokedAt) =>
        InScopeAsync(async (repo, unitOfWork) =>
        {
            var stored = (await repo.GetByIdAsync(shareId, default))!;
            stored.Revoke(revokedAt);
            await repo.UpdateAsync(stored, default);
            await unitOfWork.SaveChangesAsync(default);
        });

    private Task RevokeAllAsync(Guid dataItemId, DateTime revokedAt) =>
        InScopeAsync(async (repo, unitOfWork) =>
        {
            await repo.RevokeAllForDataItemAsync(dataItemId, revokedAt, default);
            await unitOfWork.SaveChangesAsync(default);
        });

    private Task RemoveAllForUserAsync(Guid userId) =>
        InScopeAsync(async (repo, unitOfWork) =>
        {
            await repo.RemoveAllForUserAsync(userId, default);
            await unitOfWork.SaveChangesAsync(default);
        });

    private async Task InScopeAsync(Func<IShareRepository, IUnitOfWork, Task> action)
    {
        using var scope = _factory.Services.CreateScope();
        await action(
            scope.ServiceProvider.GetRequiredService<IShareRepository>(),
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>());
    }

    private async Task<T> QueryAsync<T>(Func<IShareRepository, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<IShareRepository>());
    }
}
