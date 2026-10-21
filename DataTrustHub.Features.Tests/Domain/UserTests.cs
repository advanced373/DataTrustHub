using DataTrustHub.Domain.Users;
using DataTrustHub.SharedKernel;
using Xunit;

namespace DataTrustHub.Features.Tests.Domain;

public class UserTests
{
    [Fact]
    public void Register_CreatesRegularUser_AndPromoteToAdminChangesRoleOnce()
    {
        var user = User.Register("a@example.com", "hash");

        Assert.Equal(UserRoles.User, user.Role);
        Assert.True(user.PromoteToAdmin());
        Assert.False(user.PromoteToAdmin());
        Assert.True(user.IsAdmin);
    }

    [Fact]
    public void CheckCanBeDeletedBy_RefusesTheUserThemselves()
    {
        var user = User.CreateAdmin("admin@example.com", "hash");

        Assert.Equal(UserErrors.CannotDeleteOwnAccount, user.CheckCanBeDeletedBy(user.Id).Error);
        Assert.True(user.CheckCanBeDeletedBy(Guid.NewGuid()).IsSuccess);
    }
}
