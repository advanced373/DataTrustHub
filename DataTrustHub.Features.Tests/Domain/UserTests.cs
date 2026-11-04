using DataTrustHub.Domain.Users;
using DataTrustHub.SharedKernel;
using Xunit;

namespace DataTrustHub.Features.Tests.Domain;

public class UserTests
{
    private const string VariantEmail = " Ion@Firma.RO ";
    private const string NormalizedEmail = "ion@firma.ro";
    private const string Hash = "hash";

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
}
