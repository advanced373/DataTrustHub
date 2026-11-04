using DataTrustHub.Domain.Users;
using DataTrustHub.SharedKernel;
using Xunit;

namespace DataTrustHub.Features.Tests.Domain;

public class UserTests
{
    private const string VariantEmail = " Ion@Firma.RO ";
    private const string NormalizedEmail = "ion@firma.ro";
    private const string Hash = "hash";
    private const string NewHash = "new-hash";

    [Fact]
    public void Register_CreatesRegularUser_AndPromoteToAdminChangesRoleOnce()
    {
        var user = User.Register("a@example.com", Hash);

        Assert.Equal(UserRoles.User, user.Role);
        Assert.True(user.PromoteToAdmin());
        Assert.False(user.PromoteToAdmin());
        Assert.True(user.IsAdmin);
    }

    // AC1 of #123: the factory methods store the normalized address (U7)
    [Fact]
    public void Register_EmailWithCaseAndSurroundingSpaces_StoresTheNormalizedEmail()
    {
        var user = User.Register(VariantEmail, Hash);

        Assert.Equal(NormalizedEmail, user.Email);
    }

    [Fact]
    public void CreateAdmin_EmailWithCaseAndSurroundingSpaces_StoresTheNormalizedEmail()
    {
        var admin = User.CreateAdmin(VariantEmail, Hash);

        Assert.Equal(NormalizedEmail, admin.Email);
    }

    [Fact]
    public void CheckCanBeDeletedBy_RefusesTheUserThemselves()
    {
        var user = User.CreateAdmin("admin@example.com", Hash);

        Assert.Equal(UserErrors.CannotDeleteOwnAccount, user.CheckCanBeDeletedBy(user.Id).Error);
        Assert.True(user.CheckCanBeDeletedBy(Guid.NewGuid()).IsSuccess);
    }

    // #167: sign-in replaces a password hash in an older format with a new one
    [Fact]
    public void ReplacePasswordHash_NewHash_StoresTheNewHash()
    {
        var user = User.Register("a@example.com", Hash);

        user.ReplacePasswordHash(NewHash);

        Assert.Equal(NewHash, user.HashedPassword);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ReplacePasswordHash_NullOrEmptyHash_ThrowsAndKeepsTheStoredHash(string? emptyHash)
    {
        var user = User.Register("a@example.com", Hash);

        Assert.ThrowsAny<ArgumentException>(() => user.ReplacePasswordHash(emptyHash!));
        Assert.Equal(Hash, user.HashedPassword);
    }
}
