using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.DataItems;
using DataTrustHub.Domain.Shares;
using DataTrustHub.Features.Tests.Infrastructure.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataTrustHub.Features.Tests.Infrastructure;

// #3 (ADR-0015) on SQL Server itself (throwaway LocalDB databases; skipped where LocalDB is missing): the migrations
// build the Shares table with its columns, the foreign key to DataItems and the indexes AC6 requires; the repository round-trips every field
// with UTC times through datetime2; HasActiveAsync, translated to SQL Server SQL, agrees with Share.IsActiveAt (S5);
// RevokeAllForDataItemAsync touches only the item's not yet revoked shares; Down drops the table.
public sealed class SharesSqlServerTests(LocalDbApiFactory migrated) : IClassFixture<LocalDbApiFactory>, IDisposable
{
    private const string Table = "Shares";
    private const string DataItemsTable = "DataItems";
    private const string PreviousMigrationName = "AddRegistrationRequests";
    private const string MigrationIdSeparator = "_";
    private const string IdColumn = "Id";
    private const string DataItemIdColumn = "DataItemId";
    private const string RecipientUserIdColumn = "RecipientUserId";
    private const string SharedByUserIdColumn = "SharedByUserId";
    private const string SharedAtColumn = "SharedAt";
    private const string ExpiresAtColumn = "ExpiresAt";
    private const string RevokedAtColumn = "RevokedAt";
    private const string GuidType = "uniqueidentifier";
    private const string DateTimeType = "datetime2";
    private const string IndexColumnSeparator = ",";
    private const string ItemName = "sql-shared.txt";
    private const string ItemContent = "YWJj";
    private const long ItemSize = 3;

    private static readonly DateTime SharedAt = new(2026, 11, 11, 10, 30, 15, 123, DateTimeKind.Utc);
    private static readonly TimeSpan OneTick = TimeSpan.FromTicks(1);
    private static readonly TimeSpan OneDay = TimeSpan.FromDays(1);
    private static readonly DateTime RevokedAt = SharedAt + OneDay / 2;
    // Later than RevokedAt
    private static readonly DateTime Now = SharedAt + OneDay;

    private readonly List<string> ownDatabases = [];
    private readonly Guid ownerUserId = Guid.NewGuid();
    private readonly Guid recipientUserId = Guid.NewGuid();

    // (column, type, nullable)
    public static TheoryData<string, string, bool> Columns => new()
    {
        { IdColumn, GuidType, false },
        { DataItemIdColumn, GuidType, false },
        { RecipientUserIdColumn, GuidType, false },
        { SharedByUserIdColumn, GuidType, false },
        { SharedAtColumn, DateTimeType, false },
        { ExpiresAtColumn, DateTimeType, true },
        { RevokedAtColumn, DateTimeType, true }
    };

    // The key columns of each index AC6 requires, in key order
    public static TheoryData<string[]> Indexes => new()
    {
        new[] { RecipientUserIdColumn },
        new[] { DataItemIdColumn, RecipientUserIdColumn }
    };

    // (expiry relative to now in ticks, or null for none; revoked; expected active)
    public static TheoryData<long?, bool, bool> ActiveCases => new()
    {
        { null, false, true },
        { 1, false, true },
        { 0, false, false },
        { -1, false, false },
        { null, true, false },
        { 1, true, false }
    };

    // --- the schema the migrations build ---

    [LocalDbTheory]
    [MemberData(nameof(Columns))]
    public void MigratedDatabase_ColumnHasItsTypeAndNullability(string column, string type, bool nullable)
    {
        var row = LocalDb.Scalar<string>(migrated.Database, $"""
            SELECT CONCAT([DATA_TYPE], '|', [IS_NULLABLE])
            FROM INFORMATION_SCHEMA.COLUMNS WHERE [TABLE_NAME] = N'{Table}' AND [COLUMN_NAME] = N'{column}'
            """);

        Assert.Equal($"{type}|{(nullable ? "YES" : "NO")}", row);
    }

    [LocalDbFact]
    public void MigratedDatabase_DataItemIdReferencesDataItems()
    {
        Assert.Equal(DataItemsTable, LocalDb.Scalar<string>(migrated.Database, $"""
            SELECT OBJECT_NAME(fk.[referenced_object_id]) FROM sys.foreign_keys fk
            WHERE fk.[parent_object_id] = OBJECT_ID(N'[dbo].[{Table}]')
            """));
    }

    // Exactly one index on the table (besides the primary key) has these key columns, in this order
    [LocalDbTheory]
    [MemberData(nameof(Indexes))]
    public void MigratedDatabase_HasTheIndexOnItsColumns(string[] columns)
    {
        Assert.Equal(1, LocalDb.Scalar<int>(migrated.Database, $"""
            SELECT COUNT(*) FROM sys.indexes i
            WHERE i.[object_id] = OBJECT_ID(N'[dbo].[{Table}]') AND i.[is_primary_key] = 0
              AND (SELECT STRING_AGG(c.[name], N'{IndexColumnSeparator}') WITHIN GROUP (ORDER BY ic.[key_ordinal])
                   FROM sys.index_columns ic
                   JOIN sys.columns c ON c.[object_id] = ic.[object_id] AND c.[column_id] = ic.[column_id]
                   WHERE ic.[object_id] = i.[object_id] AND ic.[index_id] = i.[index_id] AND ic.[is_included_column] = 0)
                  = N'{string.Join(IndexColumnSeparator, columns)}'
            """));
    }

    // --- the repository on SQL Server ---

    [LocalDbFact]
    public async Task Add_ThenSave_RoundTripsEveryFieldAsUtc()
    {
        var share = Share.Create(await SaveDataItemAsync(), recipientUserId, ownerUserId, SharedAt, SharedAt + OneDay).Value;
        await SaveAsync(share);
        await RevokeAsync(share.Id);

        var loaded = (await QueryAsync(repo => repo.GetByIdAsync(share.Id, default)))!;

        Assert.Equal(
            (share.DataItemId, recipientUserId, ownerUserId, SharedAt, (DateTime?)(SharedAt + OneDay), (DateTime?)RevokedAt),
            (loaded.DataItemId, loaded.RecipientUserId, loaded.SharedByUserId, loaded.SharedAt, loaded.ExpiresAt, loaded.RevokedAt));
        Assert.Equal(
            (DateTimeKind.Utc, DateTimeKind.Utc, DateTimeKind.Utc),
            (loaded.SharedAt.Kind, loaded.ExpiresAt!.Value.Kind, loaded.RevokedAt!.Value.Kind));
    }

    [LocalDbTheory]
    [MemberData(nameof(ActiveCases))]
    public async Task HasActiveAsync_AgreesWithIsActiveAt(long? expiryTicksFromNow, bool revoked, bool expected)
    {
        var itemId = await SaveDataItemAsync();
        DateTime? expiresAt = expiryTicksFromNow is long ticks ? Now + TimeSpan.FromTicks(ticks) : null;
        var share = Share.Create(itemId, recipientUserId, ownerUserId, SharedAt, expiresAt).Value;
        await SaveAsync(share);
        if (revoked)
        {
            share.Revoke(RevokedAt);
            await RevokeAsync(share.Id);
        }

        var active = await QueryAsync(repo => repo.HasActiveAsync(itemId, recipientUserId, Now, default));

        Assert.Equal((expected, expected), (active, share.IsActiveAt(Now)));
    }

    [LocalDbFact]
    public async Task RevokeAllForDataItemAsync_RevokesOnlyTheItemsNotYetRevokedShares()
    {
        var itemId = await SaveDataItemAsync();
        var active = Share.Create(itemId, recipientUserId, ownerUserId, SharedAt, null).Value;
        var revoked = Share.Create(itemId, Guid.NewGuid(), ownerUserId, SharedAt, null).Value;
        var otherItem = Share.Create(await SaveDataItemAsync(), recipientUserId, ownerUserId, SharedAt, null).Value;
        foreach (var share in new[] { active, revoked, otherItem }) await SaveAsync(share);
        await RevokeAsync(revoked.Id);
        var laterRevocation = RevokedAt + OneDay;

        using (var scope = migrated.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IShareRepository>().RevokeAllForDataItemAsync(itemId, laterRevocation, default);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(default);
        }

        Assert.Equal(
            ((DateTime?)laterRevocation, (DateTime?)RevokedAt, (DateTime?)null),
            ((await QueryAsync(repo => repo.GetByIdAsync(active.Id, default)))!.RevokedAt,
                (await QueryAsync(repo => repo.GetByIdAsync(revoked.Id, default)))!.RevokedAt,
                (await QueryAsync(repo => repo.GetByIdAsync(otherItem.Id, default)))!.RevokedAt));
    }

    // --- Down ---

    [LocalDbFact]
    public void Down_DropsTheTable()
    {
        var database = LocalDb.NewDatabaseName();
        ownDatabases.Add(database);
        using (var context = LocalDb.Context(database)) context.Database.Migrate();

        using (var context = LocalDb.Context(database))
        {
            var id = context.Database.GetMigrations().Single(m => m.EndsWith(MigrationIdSeparator + PreviousMigrationName));
            context.GetService<IMigrator>().Migrate(id);
        }

        Assert.Equal(0, LocalDb.Scalar<int>(database, $"SELECT COUNT(*) FROM sys.tables WHERE [name] = N'{Table}'"));
    }

    public void Dispose()
    {
        LocalDb.DropAll(ownDatabases);
    }

    // --- helpers ---

    private async Task<Guid> SaveDataItemAsync()
    {
        var item = DataItem.CreatePersonal(ItemName, ItemSize, ItemContent, ownerUserId);
        using var scope = migrated.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IDataItemRepository>().Add(item);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(default);
        return item.Id;
    }

    private async Task SaveAsync(Share share)
    {
        using var scope = migrated.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IShareRepository>().Add(share);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(default);
    }

    private async Task RevokeAsync(Guid shareId)
    {
        using var scope = migrated.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IShareRepository>();
        var stored = (await repo.GetByIdAsync(shareId, default))!;
        stored.Revoke(RevokedAt);
        await repo.UpdateAsync(stored, default);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(default);
    }

    private async Task<T> QueryAsync<T>(Func<IShareRepository, Task<T>> query)
    {
        using var scope = migrated.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<IShareRepository>());
    }
}
