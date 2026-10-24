using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using System.Reflection;
using Xunit;

namespace DataTrustHub.Features.Tests.Infrastructure;

// AC4 of #24 step 1: the AddAuditRecords migration creates only the AuditRecords table and its indexes,
// with no foreign keys, and the model snapshot matches the model
public class AuditRecordsMigrationTests
{
    private const string TableName = "AuditRecords";
    private const string MigrationName = "AddAuditRecords";
    private const string MigrationIdSeparator = "_";

    // Never opened: the SQL Server provider only builds the model the migrations were generated for
    private const string UnusedSqlServerConnection = "Server=unused;Database=unused;Trusted_Connection=True;";

    [Fact]
    public void Migration_HasTheAddAuditRecordsId()
    {
        var id = typeof(AddAuditRecords).GetCustomAttribute<MigrationAttribute>()!.Id;

        Assert.EndsWith(MigrationIdSeparator + MigrationName, id);
    }

    [Fact]
    public void Up_CreatesTheAuditRecordsTableAndItsIndexesOnly()
    {
        var operations = new AddAuditRecords().UpOperations;

        Assert.All(operations, operation => Assert.True(
            operation is CreateTableOperation { Name: TableName } or CreateIndexOperation { Table: TableName },
            operation.GetType().Name));
        Assert.Single(operations.OfType<CreateTableOperation>());
        Assert.Equal(2, operations.OfType<CreateIndexOperation>().Count());
    }

    [Fact]
    public void Up_CreatesNoForeignKeys()
    {
        var operations = new AddAuditRecords().UpOperations;

        Assert.Empty(operations.OfType<CreateTableOperation>().SelectMany(t => t.ForeignKeys));
        Assert.Empty(operations.OfType<AddForeignKeyOperation>());
    }

    [Fact]
    public void Down_DropsOnlyTheAuditRecordsTable()
    {
        var operation = Assert.Single(new AddAuditRecords().DownOperations);

        Assert.Equal(TableName, Assert.IsType<DropTableOperation>(operation).Name);
    }

    [Fact]
    public void Model_HasNoChangesPendingAMigration()
    {
        var options = new DbContextOptionsBuilder<DContext>().UseSqlServer(UnusedSqlServerConnection).Options;
        using var context = new DContext(options);

        Assert.False(context.Database.HasPendingModelChanges());
    }
}
