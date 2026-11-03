using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Clearances;
using DataTrustHub.Domain.DataItems;
using DataTrustHub.Features.Tests.Infrastructure.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Model;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataTrustHub.Features.Tests.Infrastructure;

// #128 AC1, AC2 and AC4: invariant D1 (a data item is personal, with no policy and no classification priority, or
// classified, with both) is a check constraint of the model on DataItems. EF InMemory enforces no constraint, so
// the save tests run on relational SQLite, whose schema is created from the model. SQL Server itself is covered by
// DataItemClassificationCheckSqlServerTests (LocalDB) and the CI migrations job. That the snapshot carries the
// constraint is AddDataItemClassificationCheckMigrationTests.Model_HasNoChangesPendingAMigration.
public class DataItemClassificationCheckModelTests(SqliteApiFactory factory) : IClassFixture<SqliteApiFactory>
{
    internal const string ConstraintName = "CK_DataItems_Classification";
    private const string DataItemsTable = "DataItems";
    private const string PersonalMarking = DataItem.PersonalMarking;
    private const string ClassifiedMarking = "Secret";
    private const int ClassificationPriority = 3;
    private const int SqliteConstraintError = 19;

    // Never opened: the SQL Server provider only builds the model the migrations are generated from
    private const string UnusedSqlServerConnection = "Server=unused;Database=unused;Trusted_Connection=True;";

    // (has a policy, has a classification priority, is deleted, accepted): D1 accepts only both or neither,
    // whether the row is soft-deleted or not
    public static TheoryData<bool, bool, bool, bool> D1Combinations => new()
    {
        { false, false, false, true },
        { true, true, false, true },
        { true, false, false, false },
        { false, true, false, false },
        { false, false, true, true },
        { true, true, true, true },
        { true, false, true, false },
        { false, true, true, false }
    };

    // --- AC1: the model (SQL Server, as the migrations see it) ---

    [Fact]
    public void Model_DataItems_HasTheClassificationCheckConstraint()
    {
        var constraint = Assert.Single(DataItemEntityType().GetCheckConstraints());

        Assert.Equal(ConstraintName, constraint.Name);
        Assert.Equal(DataItemsTable, DataItemEntityType().GetTableName());
    }

    [Fact]
    public void Model_ClassificationCheckConstraint_UsesBothColumns()
    {
        var sql = DataItemEntityType().GetCheckConstraints().Single(c => c.Name == ConstraintName).Sql;

        Assert.Contains(nameof(DbDataItem.PolicyId), sql);
        Assert.Contains(nameof(DbDataItem.ClassificationPriority), sql);
    }

    // The only check constraint of the model: no other table gained one
    [Fact]
    public void Model_NoOtherEntity_HasACheckConstraint()
    {
        using var context = SqlServerContext();
        var withConstraints = context.GetService<IDesignTimeModel>().Model.GetEntityTypes()
            .Where(entity => entity.GetCheckConstraints().Any())
            .Select(entity => entity.ClrType);

        Assert.Equal([typeof(DbDataItem)], withConstraints);
    }

    // --- AC2: the constraint enforced on save (relational SQLite) ---

    [Theory]
    [MemberData(nameof(D1Combinations))]
    public async Task Insert_DataItemRow_IsAcceptedOnlyWhenPolicyAndPriorityAreBothSetOrBothEmpty(
        bool hasPolicy, bool hasPriority, bool isDeleted, bool accepted)
    {
        var row = Row(hasPolicy, hasPriority, isDeleted);

        var save = SaveAsync(db => db.DataItems.Add(row));

        if (accepted)
        {
            await save;
            Assert.True(await ExistsAsync(row.Id));
        }
        else
        {
            AssertBrokeTheConstraint(await Assert.ThrowsAsync<DbUpdateException>(() => save));
            Assert.False(await ExistsAsync(row.Id));
        }
    }

    [Fact]
    public async Task Update_PersonalRow_SettingOnlyAPolicy_IsRejected()
    {
        var row = Row(hasPolicy: false, hasPriority: false, isDeleted: false);
        await SaveAsync(db => db.DataItems.Add(row));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            UpdateAsync(row.Id, stored => stored.PolicyId = Guid.NewGuid()));

        AssertBrokeTheConstraint(exception);
        Assert.Null((await LoadAsync(row.Id)).PolicyId);
    }

    [Fact]
    public async Task Update_ClassifiedRow_ClearingOnlyThePriority_IsRejected()
    {
        var row = Row(hasPolicy: true, hasPriority: true, isDeleted: false);
        await SaveAsync(db => db.DataItems.Add(row));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            UpdateAsync(row.Id, stored => stored.ClassificationPriority = null));

        AssertBrokeTheConstraint(exception);
        Assert.Equal(ClassificationPriority, (await LoadAsync(row.Id)).ClassificationPriority);
    }

    // A soft-deleted item keeps the rule: deleting does not open a way to a half-classified row
    [Fact]
    public async Task Update_SoftDeletedClassifiedRow_ClearingOnlyThePolicy_IsRejected()
    {
        var row = Row(hasPolicy: true, hasPriority: true, isDeleted: true);
        await SaveAsync(db => db.DataItems.Add(row));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            UpdateAsync(row.Id, stored => stored.PolicyId = null));

        AssertBrokeTheConstraint(exception);
        Assert.NotNull((await LoadAsync(row.Id)).PolicyId);
    }

    [Fact]
    public async Task Update_PersonalRow_BecomingFullyClassified_IsAccepted()
    {
        var row = Row(hasPolicy: false, hasPriority: false, isDeleted: false);
        await SaveAsync(db => db.DataItems.Add(row));

        await UpdateAsync(row.Id, stored => (stored.PolicyId, stored.ClassificationPriority) = (Guid.NewGuid(), ClassificationPriority));

        Assert.Equal(ClassificationPriority, (await LoadAsync(row.Id)).ClassificationPriority);
    }

    // --- AC4: data items built by the domain factories still save, soft delete and read through the repository ---

    [Fact]
    public async Task PersonalDataItem_FromTheFactory_SavesSoftDeletesAndReads()
    {
        var owner = Guid.NewGuid();
        var item = DataItem.CreatePersonal("personal.txt", 3, "YWJj", owner);

        await InScopeAsync(async (items, unitOfWork) => { items.Add(item); await unitOfWork.SaveChangesAsync(default); });
        var saved = (await GetAsync(item.Id))!;
        Assert.Equal((null, null, PersonalMarking, false), (saved.PolicyId, saved.ClassificationPriority, saved.SecurityMarking, saved.IsDeleted));

        await SoftDeleteAsync(item.Id, owner);
        Assert.True((await GetAsync(item.Id))!.IsDeleted);
    }

    [Fact]
    public async Task ClassifiedDataItem_FromTheFactory_SavesSoftDeletesAndReads()
    {
        var owner = Guid.NewGuid();
        var policyId = Guid.NewGuid();
        var level = ClassificationLevel.FindStandard(ClassificationPriority)!;
        var item = DataItem.CreateClassified("classified.txt", 3, "YWJj", owner, policyId, level,
            [Clearance.Grant("clearance", owner, policyId, level)]).Value;

        await InScopeAsync(async (items, unitOfWork) => { items.Add(item); await unitOfWork.SaveChangesAsync(default); });
        var saved = (await GetAsync(item.Id))!;
        Assert.Equal((policyId, ClassificationPriority, ClassifiedMarking, false),
            (saved.PolicyId, saved.ClassificationPriority, saved.SecurityMarking, saved.IsDeleted));

        await SoftDeleteAsync(item.Id, owner);
        var deleted = (await GetAsync(item.Id))!;
        Assert.Equal((true, policyId, ClassificationPriority), (deleted.IsDeleted, deleted.PolicyId, deleted.ClassificationPriority));
    }

    // --- helpers ---

    // The violation is the D1 constraint, not some other failure, and it is not mapped to a domain error
    internal static void AssertBrokeTheConstraint(DbUpdateException exception)
    {
        var sqlite = Assert.IsType<SqliteException>(exception.InnerException);
        Assert.Equal(SqliteConstraintError, sqlite.SqliteErrorCode);
        Assert.Contains(ConstraintName, sqlite.Message);
    }

    private static DbDataItem Row(bool hasPolicy, bool hasPriority, bool isDeleted) => new()
    {
        Id = Guid.NewGuid(),
        Name = "row.txt",
        Size = 3,
        Content = "YWJj",
        OwnerUserId = Guid.NewGuid(),
        SecurityMarking = hasPolicy ? ClassifiedMarking : PersonalMarking,
        IsDeleted = isDeleted,
        PolicyId = hasPolicy ? Guid.NewGuid() : null,
        ClassificationPriority = hasPriority ? ClassificationPriority : null
    };

    private static DContext SqlServerContext() =>
        new(new DbContextOptionsBuilder<DContext>().UseSqlServer(UnusedSqlServerConnection).Options);

    private static IEntityType DataItemEntityType()
    {
        using var context = SqlServerContext();
        return context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(DbDataItem))!;
    }

    private async Task SaveAsync(Action<DContext> stage)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();
        stage(db);
        await db.SaveChangesAsync();
    }

    private async Task UpdateAsync(Guid id, Action<DbDataItem> change)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();
        change(await db.DataItems.SingleAsync(d => d.Id == id));
        await db.SaveChangesAsync();
    }

    private async Task<DbDataItem> LoadAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<DContext>().DataItems.AsNoTracking().SingleAsync(d => d.Id == id);
    }

    private async Task<bool> ExistsAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<DContext>().DataItems.AnyAsync(d => d.Id == id);
    }

    private async Task InScopeAsync(Func<IDataItemRepository, IUnitOfWork, Task> work)
    {
        using var scope = factory.Services.CreateScope();
        await work(scope.ServiceProvider.GetRequiredService<IDataItemRepository>(), scope.ServiceProvider.GetRequiredService<IUnitOfWork>());
    }

    private async Task<DataItem?> GetAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IDataItemRepository>().GetByIdAsync(id, default);
    }

    private Task SoftDeleteAsync(Guid id, Guid owner) =>
        InScopeAsync(async (items, unitOfWork) =>
        {
            var stored = (await items.GetByIdAsync(id, default))!;
            Assert.True(stored.MarkDeleted(owner, DeletionRights.WithoutOrganization).IsSuccess);
            await items.UpdateAsync(stored, default);
            await unitOfWork.SaveChangesAsync(default);
        });
}
