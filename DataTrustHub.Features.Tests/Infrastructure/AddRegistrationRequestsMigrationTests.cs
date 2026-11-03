using System.Reflection;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace DataTrustHub.Features.Tests.Infrastructure;

// #172 (spec: Infrastructure, one migration AddRegistrationRequests): the migration creates the RegistrationRequests
// table (Id primary key, Email nvarchar(256), HashedPassword nvarchar(max), RequestedAt datetime2), a unique index on
// Email (R1: one pending request per email) and an index on RequestedAt (the purge, R2); Down drops the table.
// Applying it on SQL Server is proven by RegistrationRequestsSqlServerTests (LocalDB, Windows).
public class AddRegistrationRequestsMigrationTests
{
    private const string MigrationName = "AddRegistrationRequests";
    private const string PreviousMigrationName = "AddDataItemClassificationCheck";
    private const string MigrationIdSeparator = "_";
    private const string Table = "RegistrationRequests";
    private const string IdColumn = "Id";
    private const string EmailColumn = "Email";
    private const string HashedPasswordColumn = "HashedPassword";
    private const string RequestedAtColumn = "RequestedAt";
    private const string EmailIndex = "IX_RegistrationRequests_Email";
    private const string RequestedAtIndex = "IX_RegistrationRequests_RequestedAt";
    private const int EmailMaxLength = 256;

    // Never opened: the SQL Server provider only builds the model the migrations were generated for
    private const string UnusedSqlServerConnection = "Server=unused;Database=unused;Trusted_Connection=True;";

    public static TheoryData<string, string> ColumnTypes => new()
    {
        { IdColumn, "uniqueidentifier" },
        { EmailColumn, "nvarchar(256)" },
        { HashedPasswordColumn, "nvarchar(max)" },
        { RequestedAtColumn, "datetime2" }
    };

    // --- identity and order ---

    [Fact]
    public void Migration_HasTheAddRegistrationRequestsId()
    {
        Assert.EndsWith(MigrationIdSeparator + MigrationName, MigrationId());
    }

    [Fact]
    public void Migration_ComesRightAfterAddDataItemClassificationCheck()
    {
        using var context = SqlServerContext();
        var migrations = context.Database.GetMigrations().ToList();

        var index = migrations.FindIndex(id => id.EndsWith(MigrationIdSeparator + MigrationName));

        Assert.EndsWith(MigrationIdSeparator + PreviousMigrationName, migrations[index - 1]);
    }

    [Fact]
    public void Migration_IsTheOnlyOneThatCreatesRegistrationRequests()
    {
        using var context = SqlServerContext();
        var assembly = context.GetService<IMigrationsAssembly>();

        var creating = assembly.Migrations.Keys
            .Where(id => assembly.CreateMigration(assembly.Migrations[id], context.Database.ProviderName!)
                .UpOperations.OfType<CreateTableOperation>().Any(t => t.Name == Table))
            .ToList();

        Assert.EndsWith(MigrationIdSeparator + MigrationName, Assert.Single(creating));
    }

    // --- Up ---

    [Fact]
    public void Up_IsTheTableThenItsTwoIndexes()
    {
        var operations = UpOperations();

        Assert.Equal(3, operations.Count);
        Assert.IsType<CreateTableOperation>(operations[0]);
        Assert.All(operations.Skip(1), o => Assert.IsType<CreateIndexOperation>(o));
    }

    [Fact]
    public void Up_CreatesTheTableWithIdAsPrimaryKey()
    {
        var table = CreateTable();

        Assert.Equal([IdColumn], table.PrimaryKey!.Columns);
        Assert.Equal(
            new[] { EmailColumn, HashedPasswordColumn, IdColumn, RequestedAtColumn },
            table.Columns.Select(c => c.Name).Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(ColumnTypes))]
    public void Up_ColumnHasItsTypeAndIsRequired(string column, string type)
    {
        var added = CreateTable().Columns.Single(c => c.Name == column);

        Assert.Equal((type, false), (added.ColumnType, added.IsNullable));
    }

    [Fact]
    public void Up_EmailHasTheMaximumLength()
    {
        Assert.Equal(EmailMaxLength, CreateTable().Columns.Single(c => c.Name == EmailColumn).MaxLength);
    }

    [Fact]
    public void Up_EmailIndexIsUnique()
    {
        var index = Index(EmailIndex);

        Assert.Equal((Table, true), (index.Table, index.IsUnique));
        Assert.Equal([EmailColumn], index.Columns);
    }

    [Fact]
    public void Up_RequestedAtIndexIsNotUnique()
    {
        var index = Index(RequestedAtIndex);

        Assert.Equal((Table, false), (index.Table, index.IsUnique));
        Assert.Equal([RequestedAtColumn], index.Columns);
    }

    // --- Down ---

    [Fact]
    public void Down_DropsOnlyTheTable()
    {
        var drop = Assert.IsType<DropTableOperation>(Assert.Single(DownOperations()));

        Assert.Equal(Table, drop.Name);
    }

    // --- models ---

    // Generated with dotnet ef: the step from the previous model is exactly the table and its indexes
    [Fact]
    public void TargetModel_DiffersFromThePreviousOnlyByTheRegistrationRequestsTable()
    {
        using var context = SqlServerContext();

        var differences = Differ(context).GetDifferences(
            TargetModel(context, PreviousMigrationName), TargetModel(context, MigrationName));

        Assert.All(differences, d => Assert.True(
            d is CreateTableOperation { Name: Table } or CreateIndexOperation { Table: Table }, d.GetType().Name));
        Assert.Equal(3, differences.Count);
    }

    // dotnet ef migrations has-pending-model-changes reports no changes: the snapshot carries the table
    [Fact]
    public void Model_HasNoChangesPendingAMigration()
    {
        using var context = SqlServerContext();

        Assert.False(context.Database.HasPendingModelChanges());
    }

    // --- helpers ---

    private static IReadOnlyList<MigrationOperation> UpOperations() => new AddRegistrationRequests().UpOperations;

    private static IReadOnlyList<MigrationOperation> DownOperations() => new AddRegistrationRequests().DownOperations;

    private static CreateTableOperation CreateTable() =>
        Assert.Single(UpOperations().OfType<CreateTableOperation>(), t => t.Name == Table);

    private static CreateIndexOperation Index(string name) =>
        Assert.Single(UpOperations().OfType<CreateIndexOperation>(), i => i.Name == name);

    private static string MigrationId() =>
        typeof(AddRegistrationRequests).GetCustomAttribute<MigrationAttribute>()!.Id;

    private static DContext SqlServerContext() =>
        new(new DbContextOptionsBuilder<DContext>().UseSqlServer(UnusedSqlServerConnection).Options);

    private static IMigrationsModelDiffer Differ(DContext context) => context.GetService<IMigrationsModelDiffer>();

    private static IRelationalModel TargetModel(DContext context, string migrationName)
    {
        var migrations = context.GetService<IMigrationsAssembly>();
        var id = migrations.Migrations.Keys.Single(key => key.EndsWith(MigrationIdSeparator + migrationName));
        var migration = migrations.CreateMigration(migrations.Migrations[id], context.Database.ProviderName!);
        return context.GetService<IModelRuntimeInitializer>()
            .Initialize(migration.TargetModel!, designTime: true, validationLogger: null)
            .GetRelationalModel();
    }
}
