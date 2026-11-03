using System.Net;
using System.Net.Http.Json;
using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Registrations;
using DataTrustHub.Features.Tests.Infrastructure.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataTrustHub.Features.Tests.Infrastructure;

// #172 on SQL Server itself (throwaway LocalDB databases; skipped where LocalDB is missing): the migrations build
// the RegistrationRequests table with its columns, the unique index on Email and the index on RequestedAt; a second
// request for the same email is rejected as UniqueConstraintViolationException (R1 under concurrency); the purge
// runs as SQL Server SQL; register answers 202 and 409 against the real schema; Down drops the table.
public sealed class RegistrationRequestsSqlServerTests(LocalDbApiFactory migrated)
    : IClassFixture<LocalDbApiFactory>, IDisposable
{
    private const string Table = "RegistrationRequests";
    private const string EmailIndex = "IX_RegistrationRequests_Email";
    private const string RequestedAtIndex = "IX_RegistrationRequests_RequestedAt";
    private const string PreviousMigrationName = "AddDataItemClassificationCheck";
    private const string MigrationIdSeparator = "_";
    private const string RegisterEndpoint = "/auth/register";
    private const string Password = "Password1!";
    private const string HashedPassword = "hashed-password";

    private static readonly TimeSpan OneDay = TimeSpan.FromDays(1);

    private readonly List<string> ownDatabases = [];

    // (column, type, max length in characters; -1 is max)
    public static TheoryData<string, string, int?> Columns => new()
    {
        { "Id", "uniqueidentifier", null },
        { "Email", "nvarchar", 256 },
        { "HashedPassword", "nvarchar", -1 },
        { "RequestedAt", "datetime2", null }
    };

    // --- the schema the migrations build ---

    [LocalDbTheory]
    [MemberData(nameof(Columns))]
    public void MigratedDatabase_ColumnHasItsTypeAndIsNotNull(string column, string type, int? maxLength)
    {
        var row = LocalDb.Scalar<string>(migrated.Database, $"""
            SELECT CONCAT([DATA_TYPE], '|', ISNULL(CAST([CHARACTER_MAXIMUM_LENGTH] AS nvarchar(10)), ''), '|', [IS_NULLABLE])
            FROM INFORMATION_SCHEMA.COLUMNS WHERE [TABLE_NAME] = N'{Table}' AND [COLUMN_NAME] = N'{column}'
            """);

        Assert.Equal($"{type}|{maxLength}|NO", row);
    }

    [LocalDbFact]
    public void MigratedDatabase_IdIsThePrimaryKey()
    {
        Assert.Equal("Id", LocalDb.Scalar<string>(migrated.Database, $"""
            SELECT c.[name] FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.[object_id] = i.[object_id] AND ic.[index_id] = i.[index_id]
            JOIN sys.columns c ON c.[object_id] = ic.[object_id] AND c.[column_id] = ic.[column_id]
            WHERE i.[object_id] = OBJECT_ID(N'[dbo].[{Table}]') AND i.[is_primary_key] = 1
            """));
    }

    [LocalDbTheory]
    [InlineData(EmailIndex, "Email", true)]
    [InlineData(RequestedAtIndex, "RequestedAt", false)]
    public void MigratedDatabase_HasTheIndexOnItsColumn(string index, string column, bool unique)
    {
        var row = LocalDb.Scalar<string>(migrated.Database, $"""
            SELECT CONCAT(c.[name], '|', CAST(i.[is_unique] AS int)) FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.[object_id] = i.[object_id] AND ic.[index_id] = i.[index_id]
            JOIN sys.columns c ON c.[object_id] = ic.[object_id] AND c.[column_id] = ic.[column_id]
            WHERE i.[object_id] = OBJECT_ID(N'[dbo].[{Table}]') AND i.[name] = N'{index}'
            """);

        Assert.Equal($"{column}|{(unique ? 1 : 0)}", row);
    }

    // --- R1 under concurrency ---

    [LocalDbFact]
    public async Task Save_SecondRequestForTheSameNormalizedEmail_ThrowsUniqueConstraintViolation()
    {
        var email = NewEmail();
        await SaveAsync(RegistrationRequest.Submit(email, HashedPassword, DateTime.UtcNow));

        await Assert.ThrowsAsync<UniqueConstraintViolationException>(() =>
            SaveAsync(RegistrationRequest.Submit($"  {email.ToUpperInvariant()} ", HashedPassword, DateTime.UtcNow)));
    }

    // --- the purge on SQL Server ---

    [LocalDbFact]
    public async Task RemoveExpiredAsync_DeletesOnlyTheRequestsMadeBeforeTheTime()
    {
        var cutoff = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var expired = RegistrationRequest.Submit(NewEmail(), HashedPassword, cutoff - OneDay);
        var kept = RegistrationRequest.Submit(NewEmail(), HashedPassword, cutoff);
        await SaveAsync(expired);
        await SaveAsync(kept);

        using var scope = migrated.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRegistrationRequestRepository>();
        var deleted = await repo.RemoveExpiredAsync(cutoff, default);

        Assert.Equal(1, deleted);
        Assert.Null(await repo.GetByIdAsync(expired.Id, default));
        Assert.NotNull(await repo.GetByIdAsync(kept.Id, default));
    }

    // --- register against the real schema ---

    [LocalDbFact]
    public async Task Register_NewThenSameEmail_Returns202Then409()
    {
        var client = migrated.CreateClient();
        var email = NewEmail();

        var first = await client.PostAsJsonAsync(RegisterEndpoint, new { Email = email, Password });
        var second = await client.PostAsJsonAsync(RegisterEndpoint, new { Email = email, Password });

        Assert.Equal((HttpStatusCode.Accepted, HttpStatusCode.Conflict), (first.StatusCode, second.StatusCode));
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

    private static string NewEmail() => $"sql_request_{Guid.NewGuid():N}@example.com";

    private async Task SaveAsync(RegistrationRequest request)
    {
        using var scope = migrated.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IRegistrationRequestRepository>().Add(request);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(default);
    }
}
