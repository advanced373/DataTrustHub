using DataTrustHub.Features._Shared.Seeding;
using DataTrustHub.Features.Authentication.LoginUser;
using DataTrustHub.Features.Authentication.RegisterUser;
using NetArchTest.Rules;
using Xunit;
using static DataTrustHub.Features.Tests.Architecture.ArchitectureConstants;

namespace DataTrustHub.Features.Tests.Architecture;

// ADR-0012 step 3c: register, login and the admin seeder depend on repository interfaces only
public class UserAccessArchitectureTests
{
    private static readonly Type[] UserAccessTypes =
    [
        typeof(RegisterUserHandler),
        typeof(LoginUserHandler),
        typeof(AdminSeeder)
    ];

    [Fact]
    public void UserAccessCommandsAndSeeder_ShouldNotDependOnInfrastructureOrEntityFramework()
    {
        var selection = SelectTypes();

        ArchitectureAssert.NotEmpty(selection, "the register and login handlers and the admin seeder");
        Assert.Equal(UserAccessTypes.Length, selection.GetTypes().Count());

        var result = selection
            .ShouldNot().HaveDependencyOnAny(InfrastructureNamespace, EntityFrameworkNamespace)
            .GetResult();

        ArchitectureAssert.Passed(result, "Register, login and the admin seeder must not depend on Infrastructure or EF Core");
    }

    private static PredicateList SelectTypes() =>
        UserAccessTypes.Skip(1).Aggregate(
            Types.InAssembly(FeaturesAssembly).That().HaveName(UserAccessTypes[0].Name),
            (selection, type) => selection.Or().HaveName(type.Name));
}
