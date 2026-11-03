using System.Reflection;
using System.Text.RegularExpressions;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace DataTrustHub.Features.Tests.Infrastructure;

// AC6 and AC7 of #121: the AddUniquenessIndexes migration first checks the existing data for the four rules
// (U1, O1, P2, C3) and for values over 256 characters, throwing a message that names the rule and the table and
// never a value; then it alters the three columns to nvarchar(256) and creates the four unique indexes. Down
// reverses the schema change. Running its SQL is proven only on SQL Server (the CI migrations job).
public partial class AddUniquenessIndexesMigrationTests
{
    private const string MigrationName = "AddUniquenessIndexes";
    private const string PreviousMigrationName = "NormalizeUserEmails";
    private const string NextMigrationName = "AddDataItemClassificationCheck";
    private const string NextMigrationConstraint = "CK_DataItems_Classification";
    private const string MigrationIdSeparator = "_";
    private const string KeyColumnType = "nvarchar(256)";
    private const string FormerColumnType = "nvarchar(max)";
    private const int MaxLength = 256;
    private const string MembershipsTable = "Memberships";
    private const string SqlServerThrow = "THROW";
    private const string BatchSeparator = "GO";

    // Never opened: the SQL Server provider only builds the model and the script the migrations were generated for
    private const string UnusedSqlServerConnection = "Server=unused;Database=unused;Trusted_Connection=True;";

    // (rule, table, columns of the unique index)
    private static readonly (string Rule, string Table, string[] Columns)[] Rules =
    [
        ("U1", "Users", ["Email"]),
        ("O1", "Organizations", ["Name"]),
        ("P2", "Policies", ["OrganizationId", "Name"]),
        ("C3", "Clearances", ["UserId", "PolicyId"])
    ];

    // (rule, table, column) of the columns that become nvarchar(256)
    private static readonly (string Rule, string Table, string Column)[] KeyColumns =
    [
        ("U1", "Users", "Email"),
        ("O1", "Organizations", "Name"),
        ("P2", "Policies", "Name")
    ];

    public static TheoryData<string, string, string[]> RuleData()
    {
        var data = new TheoryData<string, string, string[]>();
        foreach (var (rule, table, columns) in Rules) data.Add(rule, table, columns);
        return data;
    }

    public static TheoryData<string, string, string> KeyColumnData()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var (rule, table, column) in KeyColumns) data.Add(rule, table, column);
        return data;
    }

    public static TheoryData<string, string[]> IndexData()
    {
        var data = new TheoryData<string, string[]>();
        foreach (var (_, table, columns) in Rules) data.Add(table, columns);
        return data;
    }

    public static TheoryData<string, string> ColumnData()
    {
        var data = new TheoryData<string, string>();
        foreach (var (_, table, column) in KeyColumns) data.Add(table, column);
        return data;
    }

    // --- identity and order ---

    [Fact]
    public void Migration_HasTheAddUniquenessIndexesId()
    {
        var id = typeof(AddUniquenessIndexes).GetCustomAttribute<MigrationAttribute>()!.Id;

        Assert.EndsWith(MigrationIdSeparator + MigrationName, id);
    }

    // It was the latest migration until #128 added AddDataItemClassificationCheck after it; its place in the order stays
    [Fact]
    public void Migration_ComesRightBeforeAddDataItemClassificationCheck()
    {
        using var context = SqlServerContext();
        var migrations = context.Database.GetMigrations().ToList();

        var index = migrations.FindIndex(id => id.EndsWith(MigrationIdSeparator + MigrationName));

        Assert.EndsWith(MigrationIdSeparator + NextMigrationName, migrations[index + 1]);
    }

    [Fact]
    public void Migration_ComesRightAfterNormalizeUserEmails()
    {
        using var context = SqlServerContext();
        var migrations = context.Database.GetMigrations().ToList();

        var index = migrations.FindIndex(id => id.EndsWith(MigrationIdSeparator + MigrationName));

        Assert.EndsWith(MigrationIdSeparator + PreviousMigrationName, migrations[index - 1]);
    }

    // --- Up: the data check comes before any schema change ---

    [Fact]
    public void Up_FirstOperation_IsTheDataCheck()
    {
        var first = UpOperations().First();

        Assert.Contains(SqlServerThrow, Assert.IsType<SqlOperation>(first).Sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Up_HasOnlyTheDataCheckThenColumnChangesAndIndexes()
    {
        var operations = UpOperations();

        Assert.Single(operations.OfType<SqlOperation>());
        Assert.All(operations.Skip(1), operation => Assert.True(
            operation is AlterColumnOperation or CreateIndexOperation, operation.GetType().Name));
    }

    [Theory]
    [MemberData(nameof(RuleData))]
    public void Up_DataCheck_ThrowsForADuplicateOfTheRuleOnItsColumns(string rule, string table, string[] columns)
    {
        var groupedBy = string.Join(", ", columns.Select(Quoted));

        Assert.Contains(Checks(), check =>
            check.Query.Contains($"FROM {Quoted(table)}")
            && Regex.IsMatch(check.Query, $@"GROUP BY\s+{Regex.Escape(groupedBy)}\s+HAVING\s+COUNT\(\*\)\s*>\s*1", RegexOptions.IgnoreCase)
            && NamesRuleAndTable(check.Message, rule, table));
    }

    [Theory]
    [MemberData(nameof(KeyColumnData))]
    public void Up_DataCheck_ThrowsForAValueLongerThanTheNewLength(string rule, string table, string column)
    {
        Assert.Contains(Checks(), check =>
            check.Query.Contains($"FROM {Quoted(table)}")
            && check.Query.Contains(Quoted(column))
            && !check.Query.Contains("GROUP BY", StringComparison.OrdinalIgnoreCase)
            && NamesRuleAndTable(check.Message, rule, table));
    }

    // Every THROW of the check is one of the parsed checks, so every message was inspected
    [Fact]
    public void Up_DataCheck_EveryThrowHasALiteralMessage()
    {
        var throws = Regex.Matches(DataCheckSql(), $@"\b{SqlServerThrow}\b", RegexOptions.IgnoreCase).Count;

        Assert.Equal(Rules.Length + KeyColumns.Length, Checks().Count);
        Assert.Equal(throws, Checks().Count);
    }

    // No personal data in logs (ADR-0019 spirit): the messages are literals, never built from a stored value
    [Fact]
    public void Up_DataCheck_MessagesCarryNoValue()
    {
        var sql = DataCheckSql();

        Assert.DoesNotContain("@", sql);
        Assert.DoesNotContain("FORMATMESSAGE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CONCAT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RAISERROR", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("+", sql);
    }

    // --- Up: the schema change ---

    [Theory]
    [MemberData(nameof(ColumnData))]
    public void Up_AltersTheColumnToNvarchar256KeepingNotNull(string table, string column)
    {
        var alter = Assert.Single(UpOperations().OfType<AlterColumnOperation>(), a => a.Table == table && a.Name == column);

        Assert.Equal((KeyColumnType, MaxLength, false), (alter.ColumnType, alter.MaxLength, alter.IsNullable));
        Assert.Equal((FormerColumnType, false), (alter.OldColumn.ColumnType, alter.OldColumn.IsNullable));
    }

    [Fact]
    public void Up_AltersOnlyTheThreeKeyColumns()
    {
        var altered = UpOperations().OfType<AlterColumnOperation>().Select(a => (Table: a.Table ?? string.Empty, a.Name));

        Assert.Equal(KeyColumns.Select(k => (k.Table, k.Column)).Order().ToList(), altered.Order().ToList());
    }

    [Theory]
    [MemberData(nameof(IndexData))]
    public void Up_CreatesTheUniqueIndexOfTheRule(string table, string[] columns)
    {
        var index = Assert.Single(UpOperations().OfType<CreateIndexOperation>(), i => i.Table == table);

        Assert.True(index.IsUnique);
        Assert.Equal(columns, index.Columns);
        Assert.Null(index.Filter);
    }

    [Fact]
    public void Up_CreatesFourIndexesAndLeavesMembershipsAlone()
    {
        var operations = UpOperations();

        Assert.Equal(Rules.Length, operations.OfType<CreateIndexOperation>().Count());
        Assert.DoesNotContain(operations, operation => operation is ColumnOperation { Table: MembershipsTable }
            or CreateIndexOperation { Table: MembershipsTable } or DropIndexOperation { Table: MembershipsTable });
    }

    // The columns are altered before the indexes on them are created (an index pins its key column's type)
    [Fact]
    public void Up_AltersTheColumnsBeforeItCreatesTheIndexes()
    {
        var operations = UpOperations().ToList();

        var lastAlter = operations.FindLastIndex(o => o is AlterColumnOperation);
        var firstIndex = operations.FindIndex(o => o is CreateIndexOperation);

        Assert.True(lastAlter < firstIndex, string.Join(", ", operations.Select(o => o.GetType().Name)));
    }

    // --- Down ---

    [Fact]
    public void Down_DropsTheIndexesUpCreated()
    {
        var created = UpOperations().OfType<CreateIndexOperation>().Select(i => (i.Table, i.Name));
        var dropped = DownOperations().OfType<DropIndexOperation>().Select(i => (Table: i.Table ?? string.Empty, i.Name));

        Assert.Equal(created.Order().ToList(), dropped.Order().ToList());
    }

    [Theory]
    [MemberData(nameof(ColumnData))]
    public void Down_RestoresTheColumnToNvarcharMaxKeepingNotNull(string table, string column)
    {
        var alter = Assert.Single(DownOperations().OfType<AlterColumnOperation>(), a => a.Table == table && a.Name == column);

        Assert.Equal((FormerColumnType, (int?)null, false), (alter.ColumnType, alter.MaxLength, alter.IsNullable));
    }

    // The indexes go first: SQL Server cannot alter a column an index uses as a key
    [Fact]
    public void Down_DropsTheIndexesBeforeItAltersTheColumns()
    {
        var operations = DownOperations().ToList();

        var lastDrop = operations.FindLastIndex(o => o is DropIndexOperation);
        var firstAlter = operations.FindIndex(o => o is AlterColumnOperation);

        Assert.True(lastDrop < firstAlter, string.Join(", ", operations.Select(o => o.GetType().Name)));
    }

    [Fact]
    public void Down_IsOnlyTheReverseSchemaChange()
    {
        Assert.All(DownOperations(), operation => Assert.True(
            operation is DropIndexOperation or AlterColumnOperation, operation.GetType().Name));
        Assert.Equal(Rules.Length + KeyColumns.Length, DownOperations().Count);
    }

    // --- models ---

    // Generated with dotnet ef: the step from the previous model is exactly the three columns and four indexes
    [Fact]
    public void TargetModel_DiffersFromThePreviousOnlyByTheColumnsAndIndexes()
    {
        using var context = SqlServerContext();

        var differences = Differ(context).GetDifferences(
            TargetModel(context, PreviousMigrationName), TargetModel(context, MigrationName));

        Assert.Equal(KeyColumns.Length, differences.OfType<AlterColumnOperation>().Count());
        Assert.Equal(Rules.Length, differences.OfType<CreateIndexOperation>().Count());
        Assert.Equal(KeyColumns.Length + Rules.Length, differences.Count);
    }

    // The next migration (#128) adds only the D1 check constraint of AddDataItemClassificationCheck. Later
    // migrations change the snapshot further, so the step is checked against the next migration's target model
    [Fact]
    public void TargetModel_DiffersFromTheNextMigrationsTargetModelOnlyByTheClassificationCheck()
    {
        using var context = SqlServerContext();

        var differences = Differ(context).GetDifferences(
            TargetModel(context, MigrationName), TargetModel(context, NextMigrationName));

        var added = Assert.IsType<AddCheckConstraintOperation>(Assert.Single(differences));
        Assert.Equal(NextMigrationConstraint, added.Name);
    }

    // dotnet ef migrations has-pending-model-changes reports no changes
    [Fact]
    public void Model_HasNoChangesPendingAMigration()
    {
        using var context = SqlServerContext();

        Assert.False(context.Database.HasPendingModelChanges());
    }

    // --- the idempotent script (dotnet ef migrations script --idempotent) ---

    // Each statement runs only while the migration is not in the history, so the script can run again
    [Fact]
    public void IdempotentScript_GuardsEveryBlockOfTheMigrationByTheHistory()
    {
        var section = IdempotentSection();
        var blocks = Regex.Matches(section, @"^BEGIN\s*$", RegexOptions.Multiline).Count;
        var guards = Regex.Matches(section, $@"IF NOT EXISTS \(\s*SELECT \* FROM \[__EFMigrationsHistory\]\s*WHERE \[MigrationId\] = N'{MigrationId()}'\s*\)", RegexOptions.Singleline).Count;

        Assert.True(blocks > 0, section);
        Assert.Equal(blocks, guards);
    }

    // The check, the column changes and the indexes are one batch in one transaction: a THROW stops the batch
    // before the schema change, even when the script runs without stopping on errors
    [Fact]
    public void IdempotentScript_RunsTheMigrationAsOneBatchInATransaction()
    {
        var section = IdempotentSection();

        Assert.StartsWith("BEGIN TRANSACTION;", section.TrimStart());
        Assert.DoesNotContain(Regex.Matches(section, @"^\s*(\w+)\s*$", RegexOptions.Multiline), m => m.Groups[1].Value == BatchSeparator);
    }

    [Fact]
    public void IdempotentScript_ChecksTheDataBeforeItAltersAColumn()
    {
        var section = IdempotentSection();

        var throwAt = section.IndexOf(SqlServerThrow, StringComparison.OrdinalIgnoreCase);
        var alterAt = section.IndexOf("ALTER COLUMN", StringComparison.OrdinalIgnoreCase);

        Assert.True(throwAt >= 0 && alterAt > throwAt, section);
    }

    [Fact]
    public void IdempotentScript_RecordsTheMigrationInTheHistory()
    {
        Assert.Contains($"VALUES (N'{MigrationId()}'", IdempotentSection());
    }

    // --- helpers ---

    private sealed record Check(string Query, string Message);

    private static bool NamesRuleAndTable(string message, string rule, string table) =>
        Regex.IsMatch(message, $@"\b{rule}\b") && Regex.IsMatch(message, $@"\b{table}\b");

    private static string Quoted(string name) => $"[{name}]";

    private static IReadOnlyList<MigrationOperation> UpOperations() => new AddUniquenessIndexes().UpOperations;

    private static IReadOnlyList<MigrationOperation> DownOperations() => new AddUniquenessIndexes().DownOperations;

    private static string DataCheckSql() => ((SqlOperation)UpOperations().First()).Sql;

    private static List<Check> Checks() =>
        CheckPattern().Matches(DataCheckSql())
            .Select(m => new Check(m.Groups["query"].Value, m.Groups["message"].Value))
            .ToList();

    // IF EXISTS (<query>) THROW <number>, N'<message>', <state>;  — the message is a plain literal
    [GeneratedRegex(@"IF\s+EXISTS\s*\((?<query>.+?)\)\s*THROW\s+\d+\s*,\s*N'(?<message>[^']*)'\s*,\s*\d+\s*;",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex CheckPattern();

    private static string MigrationId() => typeof(AddUniquenessIndexes).GetCustomAttribute<MigrationAttribute>()!.Id;

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
