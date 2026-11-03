using DataTrustHub.Features.Tests.Infrastructure.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace DataTrustHub.Features.Tests.Infrastructure;

// #128 AC2 and AC3 on SQL Server itself (throwaway LocalDB databases; skipped where LocalDB is missing):
// the check constraint CK_DataItems_Classification rejects a half-classified data item, soft-deleted or not, and
// accepts personal and classified ones; the AddDataItemClassificationCheck migration refuses existing data that
// breaks D1, changing nothing, applies on an empty database and on valid data, and Down drops the constraint;
// the idempotent script runs, and runs again, on an empty database.
public sealed class DataItemClassificationCheckSqlServerTests(LocalDbApiFactory migrated)
    : IClassFixture<LocalDbApiFactory>, IDisposable
{
    private const string ConstraintName = DataItemClassificationCheckModelTests.ConstraintName;
    private const string MigrationName = "AddDataItemClassificationCheck";
    private const string PreviousMigrationName = "AddUniquenessIndexes";
    private const string MigrationIdSeparator = "_";
    private const string Rule = "D1";
    private const string DataItemsTable = "DataItems";
    private const int CheckConstraintConflict = 547;
    private const int UserDefinedError = 50000;
    private const int ClassificationPriority = 3;
    private const string BatchSeparator = "GO";

    // Databases this test created besides the shared migrated one; dropped after the test
    private readonly List<string> ownDatabases = [];

    // (has a policy, has a classification priority, is deleted, accepted): every combination runs on SQLite in
    // DataItemClassificationCheckModelTests; here SQL Server rejects a half-classified row (soft-deleted too) and
    // accepts a personal and a classified one
    public static TheoryData<bool, bool, bool, bool> D1Cases => new()
    {
        { true, false, false, false },
        { false, true, true, false },
        { false, false, false, true },
        { true, true, true, true }
    };

    // (has a policy, has a classification priority, is deleted) of the rows that break D1
    public static TheoryData<bool, bool, bool> HalfClassifiedRows => new()
    {
        { true, false, false },
        { false, true, false },
        { true, false, true },
        { false, true, true }
    };

    // --- AC2: the constraint on a database built by the migrations ---

    [LocalDbFact]
    public void MigratedDatabase_HasTheTrustedClassificationCheckOnDataItems()
    {
        Assert.Equal(1, ConstraintCount(migrated.Database, trustedAndEnabledOnly: true));
    }

    [LocalDbTheory]
    [MemberData(nameof(D1Cases))]
    public void Insert_DataItemRow_IsAcceptedOnlyWhenPolicyAndPriorityAreBothSetOrBothEmpty(
        bool hasPolicy, bool hasPriority, bool isDeleted, bool accepted)
    {
        var row = new Row(hasPolicy, hasPriority, isDeleted);

        if (accepted)
        {
            Insert(migrated.Database, row);
            Assert.Equal(1, RowCount(migrated.Database, row.Id));
        }
        else
        {
            AssertBrokeTheConstraint(Assert.Throws<SqlException>(() => Insert(migrated.Database, row)));
            Assert.Equal(0, RowCount(migrated.Database, row.Id));
        }
    }

    [LocalDbFact]
    public void Update_PersonalRow_SettingOnlyAPolicy_IsRejected()
    {
        var row = new Row(HasPolicy: false, HasPriority: false, IsDeleted: false);
        Insert(migrated.Database, row);

        var exception = Assert.Throws<SqlException>(() => LocalDb.Execute(migrated.Database,
            $"UPDATE [DataItems] SET [PolicyId] = '{Guid.NewGuid()}' WHERE [Id] = '{row.Id}'"));

        AssertBrokeTheConstraint(exception);
    }

    [LocalDbFact]
    public void Update_SoftDeletedClassifiedRow_ClearingOnlyThePriority_IsRejected()
    {
        var row = new Row(HasPolicy: true, HasPriority: true, IsDeleted: true);
        Insert(migrated.Database, row);

        var exception = Assert.Throws<SqlException>(() => LocalDb.Execute(migrated.Database,
            $"UPDATE [DataItems] SET [ClassificationPriority] = NULL WHERE [Id] = '{row.Id}'"));

        AssertBrokeTheConstraint(exception);
    }

    [LocalDbFact]
    public void Update_SoftDeletingAClassifiedRow_IsAccepted()
    {
        var row = new Row(HasPolicy: true, HasPriority: true, IsDeleted: false);
        Insert(migrated.Database, row);

        LocalDb.Execute(migrated.Database, $"UPDATE [DataItems] SET [IsDeleted] = 1 WHERE [Id] = '{row.Id}'");

        Assert.Equal(1, LocalDb.Scalar<int>(migrated.Database,
            $"SELECT COUNT(*) FROM [DataItems] WHERE [Id] = '{row.Id}' AND [IsDeleted] = 1"));
    }

    // --- AC3: the migration ---

    [LocalDbFact]
    public void Migrate_EmptyDatabase_AppliesEveryMigrationAndAddsTheConstraint()
    {
        var database = NewDatabase();

        using (var context = LocalDb.Context(database)) context.Database.Migrate();

        Assert.Equal(1, ConstraintCount(database, trustedAndEnabledOnly: true));
        Assert.EndsWith(MigrationIdSeparator + MigrationName, LastAppliedMigration(database));
    }

    [LocalDbTheory]
    [MemberData(nameof(HalfClassifiedRows))]
    public void Migrate_ExistingHalfClassifiedRow_ThrowsAndChangesNothing(bool hasPolicy, bool hasPriority, bool isDeleted)
    {
        var database = DatabaseAtPreviousMigration();
        var row = new Row(hasPolicy, hasPriority, isDeleted);
        Insert(database, row);

        var exception = Assert.Throws<SqlException>(() => MigrateToLatest(database));

        Assert.Equal(UserDefinedError, exception.Number);
        Assert.Equal(0, ConstraintCount(database, trustedAndEnabledOnly: false));
        Assert.EndsWith(MigrationIdSeparator + PreviousMigrationName, LastAppliedMigration(database));
        Assert.Equal(1, RowCount(database, row.Id));
    }

    // The message names the rule and the table, never a value or an id of the row (ADR-0019)
    [LocalDbFact]
    public void Migrate_ExistingHalfClassifiedRow_MessageNamesTheRuleAndTableButNoValue()
    {
        var database = DatabaseAtPreviousMigration();
        var row = new Row(HasPolicy: true, HasPriority: false, IsDeleted: false);
        Insert(database, row);

        var message = Assert.Throws<SqlException>(() => MigrateToLatest(database)).Message;

        Assert.Matches($@"\b{Rule}\b", message);
        Assert.Matches($@"\b{DataItemsTable}\b", message);
        foreach (var value in row.Values())
            Assert.DoesNotContain(value, message, StringComparison.OrdinalIgnoreCase);
    }

    [LocalDbFact]
    public void Migrate_ExistingPersonalAndClassifiedRows_AddsTheConstraint()
    {
        var database = DatabaseAtPreviousMigration();
        Insert(database, new Row(HasPolicy: false, HasPriority: false, IsDeleted: false));
        Insert(database, new Row(HasPolicy: true, HasPriority: true, IsDeleted: true));

        MigrateToLatest(database);

        Assert.Equal(1, ConstraintCount(database, trustedAndEnabledOnly: true));
    }

    [LocalDbFact]
    public void Down_DropsTheConstraint()
    {
        var database = NewDatabase();
        MigrateToLatest(database);

        MigrateTo(database, PreviousMigrationName);

        Assert.Equal(0, ConstraintCount(database, trustedAndEnabledOnly: false));
        Assert.EndsWith(MigrationIdSeparator + PreviousMigrationName, LastAppliedMigration(database));
        Insert(database, new Row(HasPolicy: true, HasPriority: false, IsDeleted: false));
    }

    // dotnet ef migrations script --idempotent: the script builds an empty database, and running it again
    // changes nothing and fails nothing
    [LocalDbFact]
    public void IdempotentScript_RunsTwiceOnAnEmptyDatabase()
    {
        var database = NewDatabase();
        LocalDb.Create(database);
        var batches = IdempotentScriptBatches();

        foreach (var batch in batches) LocalDb.Execute(database, batch);
        foreach (var batch in batches) LocalDb.Execute(database, batch);

        Assert.Equal(1, ConstraintCount(database, trustedAndEnabledOnly: true));
        Assert.EndsWith(MigrationIdSeparator + MigrationName, LastAppliedMigration(database));
    }

    // On a database holding a half-classified row the script's THROW stops it before the constraint
    [LocalDbFact]
    public void IdempotentScript_ExistingHalfClassifiedRow_ThrowsBeforeTheConstraint()
    {
        var database = DatabaseAtPreviousMigration();
        Insert(database, new Row(HasPolicy: false, HasPriority: true, IsDeleted: true));

        var exception = Assert.Throws<SqlException>(() =>
        {
            foreach (var batch in IdempotentScriptBatches()) LocalDb.Execute(database, batch);
        });

        Assert.Equal(UserDefinedError, exception.Number);
        Assert.Equal(0, ConstraintCount(database, trustedAndEnabledOnly: false));
        Assert.EndsWith(MigrationIdSeparator + PreviousMigrationName, LastAppliedMigration(database));
    }

    public void Dispose()
    {
        LocalDb.DropAll(ownDatabases);
    }

    // --- helpers ---

    private sealed record Row(bool HasPolicy, bool HasPriority, bool IsDeleted)
    {
        public Guid Id { get; } = Guid.NewGuid();
        public Guid OwnerUserId { get; } = Guid.NewGuid();
        public Guid? PolicyId { get; } = HasPolicy ? Guid.NewGuid() : null;

        public IEnumerable<string> Values() =>
            new[] { Id.ToString(), OwnerUserId.ToString(), PolicyId?.ToString() }.OfType<string>();
    }

    private static void Insert(string database, Row row) => LocalDb.Execute(database, $"""
        INSERT INTO [DataItems] ([Id], [Name], [Size], [Content], [OwnerUserId], [SecurityMarking], [IsDeleted], [PolicyId], [ClassificationPriority])
        VALUES ('{row.Id}', N'row.txt', 3, N'YWJj', '{row.OwnerUserId}', N'{(row.HasPolicy ? "Secret" : "UNCLASSIFIED")}',
                {(row.IsDeleted ? 1 : 0)}, {(row.PolicyId is { } policyId ? $"'{policyId}'" : "NULL")},
                {(row.HasPriority ? ClassificationPriority.ToString() : "NULL")})
        """);

    private static void AssertBrokeTheConstraint(SqlException exception)
    {
        Assert.Equal(CheckConstraintConflict, exception.Number);
        Assert.Contains(ConstraintName, exception.Message);
    }

    private static int RowCount(string database, Guid id) =>
        LocalDb.Scalar<int>(database, $"SELECT COUNT(*) FROM [DataItems] WHERE [Id] = '{id}'");

    private static int ConstraintCount(string database, bool trustedAndEnabledOnly) =>
        LocalDb.Scalar<int>(database, $"""
            SELECT COUNT(*) FROM sys.check_constraints
            WHERE [name] = N'{ConstraintName}' AND [parent_object_id] = OBJECT_ID(N'[dbo].[{DataItemsTable}]')
            {(trustedAndEnabledOnly ? "AND [is_not_trusted] = 0 AND [is_disabled] = 0" : string.Empty)}
            """);

    private static string LastAppliedMigration(string database) =>
        LocalDb.Scalar<string>(database, "SELECT TOP 1 [MigrationId] FROM [__EFMigrationsHistory] ORDER BY [MigrationId] DESC");

    private string NewDatabase()
    {
        var database = LocalDb.NewDatabaseName();
        ownDatabases.Add(database);
        return database;
    }

    private string DatabaseAtPreviousMigration()
    {
        var database = NewDatabase();
        MigrateTo(database, PreviousMigrationName);
        return database;
    }

    private static void MigrateToLatest(string database)
    {
        using var context = LocalDb.Context(database);
        context.Database.Migrate();
    }

    private static void MigrateTo(string database, string migrationName)
    {
        using var context = LocalDb.Context(database);
        var id = context.Database.GetMigrations().Single(m => m.EndsWith(MigrationIdSeparator + migrationName));
        context.GetService<IMigrator>().Migrate(id);
    }

    // The script dotnet ef writes, cut into the batches sqlcmd would send (at each GO line)
    private static List<string> IdempotentScriptBatches()
    {
        using var context = LocalDb.Context(LocalDb.NewDatabaseName());
        var script = context.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
        return System.Text.RegularExpressions.Regex.Split(script, $@"^\s*{BatchSeparator}\s*$",
                System.Text.RegularExpressions.RegexOptions.Multiline)
            .Where(batch => !string.IsNullOrWhiteSpace(batch))
            .ToList();
    }
}
