using System.Reflection;
using System.Text.RegularExpressions;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Migrations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace DataTrustHub.Features.Tests.Infrastructure;

// #128 AC3: the AddDataItemClassificationCheck migration first checks the existing data items (soft-deleted ones
// included) for rule D1, throwing a message that names the rule and the table and never a value; then it adds the
// check constraint. Down drops it. Running its SQL on SQL Server is proven by DataItemClassificationCheckSqlServerTests
// (LocalDB, Windows) and by the CI migrations job.
public partial class AddDataItemClassificationCheckMigrationTests
{
    private const string MigrationName = "AddDataItemClassificationCheck";
    private const string PreviousMigrationName = "AddUniquenessIndexes";
    private const string MigrationIdSeparator = "_";
    private const string ConstraintName = DataItemClassificationCheckModelTests.ConstraintName;
    private const string DataItemsTable = "DataItems";
    private const string Rule = "D1";
    private const string SqlServerThrow = "THROW";
    private const string BatchSeparator = "GO";
    private const string InMemorySqlite = "DataSource=:memory:";
    private const string IgnoreCheckConstraints = "PRAGMA ignore_check_constraints = ON";

    // (has a policy, has a classification priority, is deleted, accepted by D1)
    public static TheoryData<bool, bool, bool, bool> D1Combinations => DataItemClassificationCheckModelTests.D1Combinations;

    // Never opened: the SQL Server provider only builds the model and the script the migrations were generated for
    private const string UnusedSqlServerConnection = "Server=unused;Database=unused;Trusted_Connection=True;";

    // --- identity and order ---

    [Fact]
    public void Migration_HasTheAddDataItemClassificationCheckId()
    {
        Assert.EndsWith(MigrationIdSeparator + MigrationName, MigrationId());
    }

    // It was the latest migration until #172 added AddRegistrationRequests after it; its place in the order stays
    [Fact]
    public void Migration_ComesRightAfterAddUniquenessIndexes()
    {
        using var context = SqlServerContext();
        var migrations = context.Database.GetMigrations().ToList();

        var index = migrations.FindIndex(id => id.EndsWith(MigrationIdSeparator + MigrationName));

        Assert.EndsWith(MigrationIdSeparator + PreviousMigrationName, migrations[index - 1]);
    }

    // --- Up: the data check, then the constraint ---

    [Fact]
    public void Up_IsTheDataCheckThenTheCheckConstraint()
    {
        var operations = UpOperations();

        Assert.Equal(2, operations.Count);
        Assert.Contains(SqlServerThrow, Assert.IsType<SqlOperation>(operations[0]).Sql, StringComparison.OrdinalIgnoreCase);
        Assert.IsType<AddCheckConstraintOperation>(operations[1]);
    }

    [Fact]
    public void Up_AddsTheClassificationCheckConstraintOnDataItems()
    {
        var constraint = Assert.Single(UpOperations().OfType<AddCheckConstraintOperation>());

        Assert.Equal((ConstraintName, DataItemsTable), (constraint.Name, constraint.Table));
    }

    // The constraint SQL is the one of the model, so the database and the snapshot agree
    [Fact]
    public void Up_ConstraintSql_IsTheModelsConstraintSql()
    {
        using var context = SqlServerContext();
        var modelSql = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(DataTrustHub.Infrastructure.Persistance.Model.DbDataItem))!
            .GetCheckConstraints().Single(c => c.Name == ConstraintName).Sql;

        Assert.Equal(modelSql, UpOperations().OfType<AddCheckConstraintOperation>().Single().Sql);
    }

    [Fact]
    public void Up_DataCheck_HasOneCheckOnDataItems()
    {
        var check = Assert.Single(Checks());

        Assert.Contains($"FROM [{DataItemsTable}]", check.Query);
    }

    // The check's own query, run on SQLite (so on CI too) against one stored row: it finds the row exactly when
    // the row breaks D1, soft-deleted or not. The table is created from the model with its CHECK switched off,
    // so a half-classified row can be stored, as it could before this migration
    [Theory]
    [MemberData(nameof(D1Combinations))]
    public void Up_DataCheck_FindsARowExactlyWhenItBreaksD1(bool hasPolicy, bool hasPriority, bool isDeleted, bool accepted)
    {
        using var connection = new SqliteConnection(InMemorySqlite);
        connection.Open();
        using (var context = new DContext(new DbContextOptionsBuilder<DContext>().UseSqlite(connection).Options))
            context.Database.EnsureCreated();
        Execute(connection, IgnoreCheckConstraints);
        Execute(connection, $"""
            INSERT INTO [DataItems] ([Id], [Name], [Size], [Content], [OwnerUserId], [SecurityMarking], [IsDeleted], [PolicyId], [ClassificationPriority])
            VALUES ('{Guid.NewGuid()}', 'row.txt', 3, 'YWJj', '{Guid.NewGuid()}', 'Secret', {(isDeleted ? 1 : 0)},
                    {(hasPolicy ? $"'{Guid.NewGuid()}'" : "NULL")}, {(hasPriority ? "3" : "NULL")})
            """);

        using var exists = new SqliteCommand($"SELECT EXISTS ({Assert.Single(Checks()).Query})", connection);

        Assert.Equal(!accepted, Convert.ToBoolean(exists.ExecuteScalar()));
    }

    [Fact]
    public void Up_DataCheck_MessageNamesTheRuleAndTheTable()
    {
        var message = Assert.Single(Checks()).Message;

        Assert.Matches($@"\b{Rule}\b", message);
        Assert.Matches($@"\b{DataItemsTable}\b", message);
    }

    // Every THROW of the check is the parsed check, so its message was inspected
    [Fact]
    public void Up_DataCheck_EveryThrowHasALiteralMessage()
    {
        var throws = Regex.Matches(DataCheckSql(), $@"\b{SqlServerThrow}\b", RegexOptions.IgnoreCase).Count;

        Assert.Equal(throws, Checks().Count);
    }

    // No stored value or id in the message (ADR-0019 spirit): it is a literal, never built from a row
    [Fact]
    public void Up_DataCheck_MessageCarriesNoValue()
    {
        var sql = DataCheckSql();

        Assert.DoesNotContain("@", sql);
        Assert.DoesNotContain("FORMATMESSAGE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CONCAT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RAISERROR", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("+", sql);
    }

    // --- Down ---

    [Fact]
    public void Down_DropsOnlyTheClassificationCheckConstraint()
    {
        var drop = Assert.IsType<DropCheckConstraintOperation>(Assert.Single(DownOperations()));

        Assert.Equal((ConstraintName, DataItemsTable), (drop.Name, drop.Table));
    }

    // --- models ---

    // Generated with dotnet ef: the step from the previous model is exactly the check constraint
    [Fact]
    public void TargetModel_DiffersFromThePreviousOnlyByTheCheckConstraint()
    {
        using var context = SqlServerContext();

        var differences = Differ(context).GetDifferences(
            TargetModel(context, PreviousMigrationName), TargetModel(context, MigrationName));

        var added = Assert.IsType<AddCheckConstraintOperation>(Assert.Single(differences));
        Assert.Equal((ConstraintName, DataItemsTable), (added.Name, added.Table));
    }

    // dotnet ef migrations has-pending-model-changes reports no changes: the snapshot carries the constraint
    [Fact]
    public void Model_HasNoChangesPendingAMigration()
    {
        using var context = SqlServerContext();

        Assert.False(context.Database.HasPendingModelChanges());
    }

    // --- the idempotent script (dotnet ef migrations script --idempotent) ---

    [Fact]
    public void IdempotentScript_GuardsEveryBlockOfTheMigrationByTheHistory()
    {
        var section = IdempotentSection();
        var blocks = Regex.Matches(section, @"^BEGIN\s*$", RegexOptions.Multiline).Count;
        var guards = Regex.Matches(section, $@"IF NOT EXISTS \(\s*SELECT \* FROM \[__EFMigrationsHistory\]\s*WHERE \[MigrationId\] = N'{MigrationId()}'\s*\)", RegexOptions.Singleline).Count;

        Assert.True(blocks > 0, section);
        Assert.Equal(blocks, guards);
    }

    // The check and the constraint are one batch in one transaction: a THROW stops the batch before the
    // constraint, even when the script runs without stopping on errors
    [Fact]
    public void IdempotentScript_RunsTheMigrationAsOneBatchInATransaction()
    {
        var section = IdempotentSection();

        Assert.StartsWith("BEGIN TRANSACTION;", section.TrimStart());
        Assert.DoesNotContain(Regex.Matches(section, @"^\s*(\w+)\s*$", RegexOptions.Multiline), m => m.Groups[1].Value == BatchSeparator);
    }

    [Fact]
    public void IdempotentScript_ChecksTheDataBeforeItAddsTheConstraint()
    {
        var section = IdempotentSection();

        var throwAt = section.IndexOf(SqlServerThrow, StringComparison.OrdinalIgnoreCase);
        var constraintAt = section.IndexOf($"ADD CONSTRAINT [{ConstraintName}] CHECK", StringComparison.OrdinalIgnoreCase);

        Assert.True(throwAt >= 0 && constraintAt > throwAt, section);
    }

    [Fact]
    public void IdempotentScript_RecordsTheMigrationInTheHistory()
    {
        Assert.Contains($"VALUES (N'{MigrationId()}'", IdempotentSection());
    }

    // --- helpers ---

    private sealed record Check(string Query, string Message);

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = new SqliteCommand(sql, connection);
        command.ExecuteNonQuery();
    }

    private static IReadOnlyList<MigrationOperation> UpOperations() => new AddDataItemClassificationCheck().UpOperations;

    private static IReadOnlyList<MigrationOperation> DownOperations() => new AddDataItemClassificationCheck().DownOperations;

    private static string DataCheckSql() => ((SqlOperation)UpOperations().First()).Sql;

    private static List<Check> Checks() =>
        CheckPattern().Matches(DataCheckSql())
            .Select(m => new Check(m.Groups["query"].Value, m.Groups["message"].Value))
            .ToList();

    // IF EXISTS (<query>) THROW <number>, N'<message>', <state>;  — the message is a plain literal
    [GeneratedRegex(@"IF\s+EXISTS\s*\((?<query>.+?)\)\s*THROW\s+\d+\s*,\s*N'(?<message>[^']*)'\s*,\s*\d+\s*;",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex CheckPattern();

    private static string MigrationId() =>
        typeof(AddDataItemClassificationCheck).GetCustomAttribute<MigrationAttribute>()!.Id;

    // The part of the idempotent script for this migration: from its transaction to its commit
    private static string IdempotentSection()
    {
        using var context = SqlServerContext();
        var script = context.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
        var guard = script.IndexOf($"N'{MigrationId()}'", StringComparison.Ordinal);
        var start = script.LastIndexOf("BEGIN TRANSACTION;", guard, StringComparison.Ordinal);
        var end = script.IndexOf("COMMIT;", guard, StringComparison.Ordinal);
        return script[start..end];
    }

    private static DContext SqlServerContext() =>
        new(new DbContextOptionsBuilder<DContext>().UseSqlServer(UnusedSqlServerConnection).Options);

    private static IMigrationsModelDiffer Differ(DContext context) => context.GetService<IMigrationsModelDiffer>();

    private static IRelationalModel TargetModel(DContext context, string migrationName)
    {
        var migrations = context.GetService<IMigrationsAssembly>();
        var id = migrations.Migrations.Keys.Single(key => key.EndsWith(MigrationIdSeparator + migrationName));
        var migration = migrations.CreateMigration(migrations.Migrations[id], context.Database.ProviderName!);
        return Relational(context, migration.TargetModel!);
    }

    private static IRelationalModel Relational(DContext context, IModel model) =>
        context.GetService<IModelRuntimeInitializer>().Initialize(model, designTime: true, validationLogger: null)
            .GetRelationalModel();
}
