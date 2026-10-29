using System.Reflection;
using System.Text.RegularExpressions;
using DataTrustHub.Domain.Users;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace DataTrustHub.Features.Tests.Infrastructure;

// AC9 of #123: the NormalizeUserEmails migration is data only (no schema change) and checks for addresses that
// would collide before it updates. Running its SQL is proven only on SQL Server (the CI migrations job)
public partial class NormalizeUserEmailsMigrationTests
{
    private const string MigrationName = "NormalizeUserEmails";
    private const string PreviousMigrationName = "AddAuditRecords";
    private const string MigrationIdSeparator = "_";
    private const string UsersTable = "[Users]";
    private const string SqlServerThrow = "THROW";
    private const string SqlUpdate = "UPDATE";
    private const string SqlLower = "LOWER(";
    private const string TrimWithTheSetFromEmail = "TRIM(@whitespace FROM [Email]";
    private const string ProbeAddress = "ion@firma.ro";

    // Never opened: the SQL Server provider only builds the model the migrations were generated for
    private const string UnusedSqlServerConnection = "Server=unused;Database=unused;Trusted_Connection=True;";

    [Fact]
    public void Migration_HasTheNormalizeUserEmailsId()
    {
        var id = typeof(NormalizeUserEmails).GetCustomAttribute<MigrationAttribute>()!.Id;

        Assert.EndsWith(MigrationIdSeparator + MigrationName, id);
    }

    [Fact]
    public void Migration_IsTheLatestMigration()
    {
        using var context = SqlServerContext();

        var latest = context.Database.GetMigrations().Last();

        Assert.EndsWith(MigrationIdSeparator + MigrationName, latest);
    }

    [Fact]
    public void Up_IsOnlySqlOnTheUsersTable()
    {
        var operation = Assert.Single(new NormalizeUserEmails().UpOperations);

        Assert.Contains(UsersTable, Assert.IsType<SqlOperation>(operation).Sql);
    }

    // The collision check (which fails the migration) comes before the update, so a failure changes nothing
    [Fact]
    public void Up_ChecksForCollisionsBeforeItUpdates()
    {
        var sql = ((SqlOperation)new NormalizeUserEmails().UpOperations.Single()).Sql;

        var throwAt = sql.IndexOf(SqlServerThrow, StringComparison.OrdinalIgnoreCase);
        var updateAt = sql.IndexOf(SqlUpdate, StringComparison.OrdinalIgnoreCase);

        Assert.True(throwAt >= 0 && updateAt > throwAt, sql);
    }

    // The SQL trims exactly the characters EmailAddress.From trims (U7), so a stored email gets the same form
    // the application compares against
    [Fact]
    public void Up_TrimSet_IsTheWhitespaceEmailAddressTrims()
    {
        var trimmedByTheValueObject = Enumerable.Range(char.MinValue, char.MaxValue + 1)
            .Where(code => EmailAddress.From((char)code + ProbeAddress + (char)code).Value == ProbeAddress);

        Assert.Equal(trimmedByTheValueObject.Order(), DeclaredTrimCodes().Order());
    }

    // A declared length shorter than the set would silently cut characters off the end of the trim set
    [Fact]
    public void Up_TrimSetVariable_IsLongEnoughForTheWholeSet()
    {
        var declaredLength = int.Parse(TrimSetDeclaration().Match(UpSql()).Groups[1].Value);

        Assert.True(declaredLength >= DeclaredTrimCodes().Count(), UpSql());
    }

    // The collision check groups by the same normalization the update applies, through the trim set
    [Fact]
    public void Up_CollisionCheckAndUpdate_ApplyTheSameTrimAndLowerCase()
    {
        var grouped = GroupByExpression().Match(UpSql()).Groups[1].Value.Trim();
        var updated = SetEmailExpression().Match(UpSql()).Groups[1].Value.Trim();

        Assert.Equal(grouped, updated);
        Assert.StartsWith(SqlLower, updated);
        Assert.Contains(TrimWithTheSetFromEmail, updated);
    }

    [Fact]
    public void Down_ChangesNoSchema()
    {
        Assert.DoesNotContain(new NormalizeUserEmails().DownOperations, operation => operation is not SqlOperation);
    }

    [Fact]
    public void TargetModel_IsTheSameAsThePreviousMigrations()
    {
        using var context = SqlServerContext();

        Assert.False(Differ(context).HasDifferences(
            TargetModel(context, PreviousMigrationName), TargetModel(context, MigrationName)));
    }

    [Fact]
    public void TargetModel_IsTheSameAsTheModelSnapshot()
    {
        using var context = SqlServerContext();
        var snapshot = context.GetService<IMigrationsAssembly>().ModelSnapshot!.Model;

        Assert.False(Differ(context).HasDifferences(TargetModel(context, MigrationName), Relational(context, snapshot)));
    }

    // No pending model changes: AuditRecordsMigrationTests.Model_HasNoChangesPendingAMigration covers the whole model

    private static string UpSql() => ((SqlOperation)new NormalizeUserEmails().UpOperations.Single()).Sql;

    private static IEnumerable<int> DeclaredTrimCodes() =>
        NCharCode().Matches(UpSql()).Select(match => int.Parse(match.Groups[1].Value));

    [GeneratedRegex(@"NCHAR\((\d+)\)")]
    private static partial Regex NCharCode();

    [GeneratedRegex(@"@whitespace\s+nvarchar\((\d+)\)", RegexOptions.IgnoreCase)]
    private static partial Regex TrimSetDeclaration();

    [GeneratedRegex(@"GROUP BY\s+(.+?)\s+HAVING", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex GroupByExpression();

    [GeneratedRegex(@"SET\s+\[Email\]\s*=\s*(.+?);", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex SetEmailExpression();

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
