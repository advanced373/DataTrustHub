using DataTrustHub.Features.Tests.Infrastructure.Helpers;
using Xunit;

namespace DataTrustHub.Features.Tests.Infrastructure;

// The LocalDB helper creates and drops only its own throwaway databases; any other name is refused before a
// connection is opened, so these run everywhere, LocalDB or not
public class LocalDbGuardTests
{
    private const string RealDatabase = "DataTrustHub";
    private const string SystemDatabase = "master";
    private const string Hex32 = "0123456789abcdef0123456789abcdef";
    private const string ThrowawayDatabase = "DataTrustHub_Tests_" + Hex32;
    private const string WrongCase = "datatrusthub_tests_" + Hex32;
    private const string UppercaseHex = "DataTrustHub_Tests_0123456789ABCDEF0123456789ABCDEF";
    private const string WithBracket = "DataTrustHub_Tests_" + Hex32 + "]";
    private const string QuoteInjection = "DataTrustHub_Tests_x') IS NULL DROP DATABASE y;--";
    private const string QuoteAfterValidName = "DataTrustHub_Tests_" + Hex32 + "'";
    private const string TrailingNewline = "DataTrustHub_Tests_" + Hex32 + "\n";
    private const string PrefixOnly = "DataTrustHub_Tests_";
    private const string FreeSuffix = "DataTrustHub_Tests_anything";

    [Theory]
    [InlineData(RealDatabase)]
    [InlineData(SystemDatabase)]
    [InlineData(WrongCase)]
    [InlineData(UppercaseHex)]
    [InlineData(WithBracket)]
    [InlineData(QuoteInjection)]
    [InlineData(QuoteAfterValidName)]
    [InlineData(TrailingNewline)]
    [InlineData(PrefixOnly)]
    [InlineData(FreeSuffix)]
    public void Drop_NameNotOfTheThrowawayShape_Throws(string database)
    {
        Assert.Throws<ArgumentException>(() => LocalDb.Drop(database));
    }

    [Theory]
    [InlineData(RealDatabase)]
    [InlineData(SystemDatabase)]
    [InlineData(WrongCase)]
    [InlineData(UppercaseHex)]
    [InlineData(WithBracket)]
    [InlineData(QuoteInjection)]
    [InlineData(QuoteAfterValidName)]
    [InlineData(TrailingNewline)]
    [InlineData(PrefixOnly)]
    [InlineData(FreeSuffix)]
    public void Create_NameNotOfTheThrowawayShape_Throws(string database)
    {
        Assert.Throws<ArgumentException>(() => LocalDb.Create(database));
    }

    [Fact]
    public void IsThrowaway_NewDatabaseName_IsTrue()
    {
        Assert.True(LocalDb.IsThrowaway(LocalDb.NewDatabaseName()));
    }

    [Fact]
    public void IsThrowaway_ExactShape_IsTrue()
    {
        Assert.True(LocalDb.IsThrowaway(ThrowawayDatabase));
    }

    // The sweep's LIKE is case-insensitive on the server: whatever it lists that is not of the exact shape is skipped,
    // not passed on to Drop (where the guard would throw and break every LocalDB test)
    [Fact]
    public void Throwaways_ListWithOtherNames_KeepsOnlyTheThrowawayShape()
    {
        var listed = new[] { WrongCase, ThrowawayDatabase, WithBracket, QuoteInjection, UppercaseHex, FreeSuffix };

        var throwaways = LocalDb.Throwaways(listed);

        Assert.Equal([ThrowawayDatabase], throwaways);
    }

    [Fact]
    public void Throwaways_OnlyOtherNames_IsEmptyAndDoesNotThrow()
    {
        var throwaways = LocalDb.Throwaways([WrongCase, WithBracket, QuoteInjection]);

        Assert.Empty(throwaways);
    }

    [Fact]
    public void DropAll_ThrowawaysOfAListWithOtherNames_DoesNotThrow()
    {
        var listed = new[] { WrongCase, WithBracket, QuoteInjection };

        var exception = Record.Exception(() => LocalDb.DropAll(LocalDb.Throwaways(listed)));

        Assert.Null(exception);
    }

    // On LocalDB: a quote that would break out of the SQL text is refused, and a throwaway database named in the
    // injected statement (created by this test, never a real one) is still there afterwards
    [LocalDbFact]
    public void Drop_QuoteInjectionNamingAThrowawayVictim_RefusesAndVictimSurvives()
    {
        var victim = LocalDb.NewDatabaseName();
        LocalDb.Create(victim);
        try
        {
            var injection = $"DataTrustHub_Tests_x') IS NULL DROP DATABASE {victim};--";

            Assert.Throws<ArgumentException>(() => LocalDb.Drop(injection));

            Assert.Equal(1, DatabaseCount(victim));
        }
        finally
        {
            LocalDb.Drop(victim);
        }
    }

    // On LocalDB: Create and Drop work through the parameterized, QUOTENAME'd statements
    [LocalDbFact]
    public void CreateThenDrop_ThrowawayDatabase_CreatesAndRemovesIt()
    {
        var database = LocalDb.NewDatabaseName();

        LocalDb.Create(database);
        var existsAfterCreate = DatabaseCount(database);
        LocalDb.Drop(database);

        Assert.Equal(1, existsAfterCreate);
        Assert.Equal(0, DatabaseCount(database));
    }

    // victim and database come from NewDatabaseName, so they are safe to embed in this lookup
    private static int DatabaseCount(string database) =>
        LocalDb.Scalar<int>(SystemDatabase, $"SELECT COUNT(*) FROM sys.databases WHERE [name] = N'{database}'");
}
