using System.Globalization;
using DataTrustHub.Domain.Users;
using Xunit;

namespace DataTrustHub.Features.Tests.Domain;

// AC1 of #123: the email address value object holds the normalization rule (U7): surrounding whitespace
// trimmed and the whole address lower-cased with the invariant culture
public class EmailAddressTests
{
    private const string NormalizedEmail = "ion@firma.ro";
    private const string TurkishCulture = "tr-TR";
    private const string AddressWithCapitalI = "IONEL@FIRMA.RO";
    private const string InvariantLowerCaseOfCapitalI = "ionel@firma.ro";
    private const string AddressWithInnerSpace = " Ion Pop@Firma.RO ";
    private const string InnerSpaceKeptNormalized = "ion pop@firma.ro";

    [Theory]
    [InlineData(" Ion@Firma.RO ")]
    [InlineData("ION@FIRMA.RO")]
    [InlineData("Ion@firma.ro")]
    [InlineData("ion@Firma.Ro")]
    [InlineData("   ion@firma.ro")]
    [InlineData("ion@firma.ro   ")]
    [InlineData("\tIon@Firma.RO\r\n")]
    public void From_CaseOrSurroundingWhitespaceVariant_ReturnsTrimmedLowerCaseAddress(string email)
    {
        Assert.Equal(NormalizedEmail, EmailAddress.From(email).Value);
    }

    [Fact]
    public void From_NormalizedAddress_ReturnsItUnchanged()
    {
        Assert.Equal(NormalizedEmail, EmailAddress.From(NormalizedEmail).Value);
    }

    [Fact]
    public void From_AppliedTwice_GivesTheSameValue()
    {
        var once = EmailAddress.From(" Ion@Firma.RO ");

        Assert.Equal(once, EmailAddress.From(once.Value));
    }

    [Fact]
    public void From_TwoVariantsOfOneAddress_AreEqual()
    {
        Assert.Equal(EmailAddress.From(" Ion@Firma.RO "), EmailAddress.From("ion@firma.ro"));
    }

    [Fact]
    public void From_DifferentAddresses_AreNotEqual()
    {
        Assert.NotEqual(EmailAddress.From("ion@firma.ro"), EmailAddress.From("ion@firma.com"));
    }

    [Fact]
    public void From_InnerWhitespace_IsKept()
    {
        Assert.Equal(InnerSpaceKeptNormalized, EmailAddress.From(AddressWithInnerSpace).Value);
    }

    // The rule uses the invariant culture: under tr-TR, "I" must still become "i", not the dotless "ı"
    [Fact]
    public void From_UnderTurkishCulture_LowerCasesWithTheInvariantCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(TurkishCulture);

            Assert.Equal(InvariantLowerCaseOfCapitalI, EmailAddress.From(AddressWithCapitalI).Value);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void ToString_ReturnsTheNormalizedAddress()
    {
        Assert.Equal(NormalizedEmail, EmailAddress.From(" Ion@Firma.RO ").ToString());
    }
}
