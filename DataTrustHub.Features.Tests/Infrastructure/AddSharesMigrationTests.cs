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

// #3 (ADR-0015, spec: one migration AddShares): the migration creates the Shares table (Id primary key, the data item,
// recipient and sharer ids, SharedAt required, ExpiresAt and RevokedAt optional, every time datetime2) with a foreign
// key to DataItems and the indexes on RecipientUserId and on (DataItemId, RecipientUserId) (AC6); Down drops the
// table; the snapshot carries it, so the model has no pending changes.
// Applying it on SQL Server is proven by SharesSqlServerTests (LocalDB, Windows).
public class AddSharesMigrationTests
{
    private const string MigrationName = "AddShares";
    private const string PreviousMigrationName = "AddRegistrationRequests";
    private const string MigrationIdSeparator = "_";
    private const string Table = "Shares";
    private const string DataItemsTable = "DataItems";
    private const string IdColumn = "Id";
    private const string DataItemIdColumn = "DataItemId";
    private const string RecipientUserIdColumn = "RecipientUserId";
    private const string SharedByUserIdColumn = "SharedByUserId";
    private const string SharedAtColumn = "SharedAt";
    private const string ExpiresAtColumn = "ExpiresAt";
    private const string RevokedAtColumn = "RevokedAt";
    private const string GuidType = "uniqueidentifier";
    private const string DateTimeType = "datetime2";

    // Never opened: the SQL Server provider only builds the model the migrations were generated for
    private const string UnusedSqlServerConnection = "Server=unused;Database=unused;Trusted_Connection=True;";

    // The key columns of each index AC6 requires, in key order
    public static TheoryData<string[]> Indexes => new()
    {
        new[] { RecipientUserIdColumn },
        new[] { DataItemIdColumn, RecipientUserIdColumn }
    };

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

    // --- identity and order ---

    [Fact]
    public void Migration_HasTheAddSharesId()
    {
        Assert.EndsWith(MigrationIdSeparator + MigrationName,
            typeof(AddShares).GetCustomAttribute<MigrationAttribute>()!.Id);
    }

    [Fact]
    public void Migration_ComesRightAfterAddRegistrationRequests()
    {
        using var context = SqlServerContext();
        var migrations = context.Database.GetMigrations().ToList();

        var index = migrations.FindIndex(id => id.EndsWith(MigrationIdSeparator + MigrationName));

        Assert.EndsWith(MigrationIdSeparator + PreviousMigrationName, migrations[index - 1]);
    }

    [Fact]
    public void Migration_IsTheOnlyOneThatCreatesShares()
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
    public void Up_CreatesTheTableWithIdAsPrimaryKeyAndEveryColumn()
    {
        var table = CreateTable();

        Assert.Equal([IdColumn], table.PrimaryKey!.Columns);
        Assert.Equal(
            new[] { DataItemIdColumn, ExpiresAtColumn, IdColumn, RecipientUserIdColumn, RevokedAtColumn, SharedAtColumn, SharedByUserIdColumn },
            table.Columns.Select(c => c.Name).Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Columns))]
    public void Up_ColumnHasItsTypeAndNullability(string column, string type, bool nullable)
    {
        var added = CreateTable().Columns.Single(c => c.Name == column);

        Assert.Equal((type, nullable), (added.ColumnType, added.IsNullable));
    }

    [Fact]
    public void Up_DataItemIdReferencesDataItems()
    {
        var foreignKey = Assert.Single(CreateTable().ForeignKeys);

        Assert.Equal(DataItemsTable, foreignKey.PrincipalTable);
        Assert.Equal([DataItemIdColumn], foreignKey.Columns);
        Assert.Equal([IdColumn], foreignKey.PrincipalColumns!);
    }

    [Theory]
    [MemberData(nameof(Indexes))]
    public void Up_CreatesTheIndexOnItsColumns(string[] columns)
    {
        Assert.Single(new AddShares().UpOperations.OfType<CreateIndexOperation>(),
            i => i.Table == Table && i.Columns.SequenceEqual(columns));
    }

    [Theory]
    [MemberData(nameof(Indexes))]
    public void Model_HasTheIndexOnItsColumns(string[] columns)
    {
        using var context = SqlServerContext();
        var shares = context.Model.GetRelationalModel().Tables.Single(t => t.Name == Table);

        Assert.Single(shares.Indexes, i => i.Columns.Select(c => c.Name).SequenceEqual(columns));
    }

    [Fact]
    public void Up_ChangesOnlyTheSharesTable()
    {
        Assert.All(new AddShares().UpOperations, o => Assert.True(
            o is CreateTableOperation { Name: Table } or CreateIndexOperation { Table: Table }, o.GetType().Name));
    }

    // --- Down ---

    [Fact]
    public void Down_DropsOnlyTheTable()
    {
        var drop = Assert.IsType<DropTableOperation>(Assert.Single(new AddShares().DownOperations));

        Assert.Equal(Table, drop.Name);
    }

    // --- models ---

    // Generated with dotnet ef: the step from the previous model touches only the Shares table
    [Fact]
    public void TargetModel_DiffersFromThePreviousOnlyByTheSharesTable()
    {
        using var context = SqlServerContext();

        var differences = Differ(context).GetDifferences(
            TargetModel(context, PreviousMigrationName), TargetModel(context, MigrationName));

        Assert.NotEmpty(differences);
        Assert.All(differences, d => Assert.True(
            d is CreateTableOperation { Name: Table } or CreateIndexOperation { Table: Table }, d.GetType().Name));
    }

    // dotnet ef migrations has-pending-model-changes reports no changes: the snapshot carries the table
    [Fact]
    public void Model_HasNoChangesPendingAMigration()
    {
        using var context = SqlServerContext();

        Assert.False(context.Database.HasPendingModelChanges());
    }

    // The last migration's target model is the current model: AddShares is what brings Shares into it
    [Fact]
    public void Model_HasTheSharesTable()
    {
        using var context = SqlServerContext();

        Assert.Contains(context.Model.GetRelationalModel().Tables, t => t.Name == Table);
    }

    // --- helpers ---

    private static CreateTableOperation CreateTable() =>
        Assert.Single(new AddShares().UpOperations.OfType<CreateTableOperation>(), t => t.Name == Table);

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
