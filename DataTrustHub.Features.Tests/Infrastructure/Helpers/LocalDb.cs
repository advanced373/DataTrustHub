using DataTrustHub.Infrastructure.Persistance;
using System.Data;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DataTrustHub.Features.Tests.Infrastructure.Helpers;

// Throwaway SQL Server LocalDB databases for tests that need SQL Server itself (check constraints, migration SQL).
// LocalDB exists only on Windows developer machines; elsewhere (the CI build-and-test job) the tests are skipped
// and the CI migrations job applies the migrations to a real SQL Server instead.
internal static class LocalDb
{
    internal const string NotAvailable = "SQL Server LocalDB is not available on this machine";

    private const string Server = @"(localdb)\MSSQLLocalDB";
    private const string Master = "master";
    private const string DatabasePrefix = "DataTrustHub_Tests_";
    private const int StaleAfterHours = 12;
    private const string NameParameter = "@name";
    private const int SysnameLength = 128;

    private static readonly Regex ThrowawayName =
        new($@"\A{DatabasePrefix}[0-9a-f]{{32}}\z", RegexOptions.CultureInvariant);

    private static readonly Lazy<bool> Available = new(CanConnect);

    internal static bool IsAvailable => Available.Value;

    internal static string NewDatabaseName() => DatabasePrefix + Guid.NewGuid().ToString("N");

    internal static string ConnectionString(string database) =>
        new SqlConnectionStringBuilder
        {
            DataSource = Server,
            InitialCatalog = database,
            IntegratedSecurity = true,
            TrustServerCertificate = true,
            ConnectTimeout = 60
        }.ConnectionString;

    internal static DContext Context(string database) =>
        new(new DbContextOptionsBuilder<DContext>().UseSqlServer(ConnectionString(database)).Options);

    internal static void Create(string database)
    {
        EnsureThrowaway(database);
        ExecuteWithName(Master, """
            DECLARE @quoted nvarchar(258) = QUOTENAME(@name);
            EXEC (N'CREATE DATABASE ' + @quoted);
            """, database);
    }

    // Closes every connection to the database first, so a pooled connection does not keep it alive. The name goes
    // in as a SQL parameter and is bracketed by QUOTENAME, never pasted into the SQL text
    internal static void Drop(string database)
    {
        EnsureThrowaway(database);
        SqlConnection.ClearAllPools();
        ExecuteWithName(Master, """
            IF DB_ID(@name) IS NOT NULL
            BEGIN
                DECLARE @quoted nvarchar(258) = QUOTENAME(@name);
                EXEC (N'ALTER DATABASE ' + @quoted + N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE');
                EXEC (N'DROP DATABASE ' + @quoted);
            END
            """, database);
    }

    // Tries to drop every database, even when one drop fails; the failures are thrown together at the end
    internal static void DropAll(IEnumerable<string> databases)
    {
        var failures = new List<Exception>();
        foreach (var database in databases)
        {
            try
            {
                Drop(database);
            }
            catch (Exception exception) when (exception is SqlException or InvalidOperationException)
            {
                failures.Add(exception);
            }
        }
        if (failures.Count > 0) throw new AggregateException(failures);
    }

    // True only for the exact shape NewDatabaseName produces: the prefix, case-sensitive, then 32 lowercase hex digits
    internal static bool IsThrowaway(string database) => ThrowawayName.IsMatch(database);

    // The names a sweep may drop: anything else the server lists (another case, a quote, a bracket) is skipped
    internal static List<string> Throwaways(IEnumerable<string> databases) => databases.Where(IsThrowaway).ToList();

    // Only a database this helper names may be created or dropped: never a real one, whatever a caller passes
    private static void EnsureThrowaway(string database)
    {
        if (!IsThrowaway(database))
            throw new ArgumentException($"Only throwaway test databases ({DatabasePrefix}<32 hex digits>) may be created or dropped", nameof(database));
    }

    private static void ExecuteWithName(string database, string sql, string name)
    {
        using var connection = new SqlConnection(ConnectionString(database));
        connection.Open();
        using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(NameParameter, SqlDbType.NVarChar, SysnameLength).Value = name;
        command.ExecuteNonQuery();
    }

    internal static void Execute(string database, string sql)
    {
        using var connection = new SqlConnection(ConnectionString(database));
        connection.Open();
        using var command = new SqlCommand(sql, connection);
        command.ExecuteNonQuery();
    }

    internal static T Scalar<T>(string database, string sql)
    {
        using var connection = new SqlConnection(ConnectionString(database));
        connection.Open();
        using var command = new SqlCommand(sql, connection);
        return (T)command.ExecuteScalar()!;
    }

    private static bool CanConnect()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            Scalar<int>(Master, "SELECT 1");
            DropStaleDatabases();
            return true;
        }
        catch (SqlException)
        {
            return false;
        }
    }

    // Best effort, once per test run: drops throwaway databases a crashed earlier run left behind. Only those older
    // than StaleAfterHours, so a run going on at the same time (another worktree) keeps its databases. The LIKE follows
// the server collation (case-insensitive by default), so its result is filtered through IsThrowaway before any drop
    private static void DropStaleDatabases()
    {
        try
        {
            DropAll(StaleDatabases());
        }
        catch (Exception exception) when (exception is SqlException or AggregateException)
        {
            // A stale database still in use, or a failed lookup, is left; the next run tries again
        }
    }

    private static List<string> StaleDatabases()
    {
        using var connection = new SqlConnection(ConnectionString(Master));
        connection.Open();
        using var command = new SqlCommand(
            "SELECT [name] FROM sys.databases WHERE [name] LIKE @prefix AND [create_date] < DATEADD(HOUR, -@hours, GETDATE())",
            connection);
        command.Parameters.AddWithValue("@prefix", DatabasePrefix.Replace("_", "[_]") + "%");
        command.Parameters.AddWithValue("@hours", StaleAfterHours);
        using var reader = command.ExecuteReader();
        var stale = new List<string>();
        while (reader.Read()) stale.Add(reader.GetString(0));
        return Throwaways(stale);
    }
}

// A fact that runs only where SQL Server LocalDB is available; skipped (and reported as skipped) elsewhere
internal sealed class LocalDbFactAttribute : FactAttribute
{
    public LocalDbFactAttribute()
    {
        if (!LocalDb.IsAvailable) Skip = LocalDb.NotAvailable;
    }
}

internal sealed class LocalDbTheoryAttribute : TheoryAttribute
{
    public LocalDbTheoryAttribute()
    {
        if (!LocalDb.IsAvailable) Skip = LocalDb.NotAvailable;
    }
}
