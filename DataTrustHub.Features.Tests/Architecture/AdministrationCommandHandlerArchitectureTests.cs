using DataTrustHub.Features.Administration.AddMember;
using DataTrustHub.Features.Administration.AppointOrganizationAdmin;
using DataTrustHub.Features.Administration.CreateAccount;
using DataTrustHub.Features.Administration.CreateClearance;
using DataTrustHub.Features.Administration.CreateOrganization;
using DataTrustHub.Features.Administration.CreatePolicy;
using DataTrustHub.Features.Administration.DeleteAccount;
using DataTrustHub.Features.Administration.DeleteClearance;
using DataTrustHub.Features.Administration.DeleteOrganization;
using DataTrustHub.Features.Administration.DeletePolicy;
using NetArchTest.Rules;
using Xunit;
using static DataTrustHub.Features.Tests.Architecture.ArchitectureConstants;

namespace DataTrustHub.Features.Tests.Architecture;

// ADR-0012 steps 3b-1 and 3b-2: the Administration command handlers depend on repository interfaces only
public class AdministrationCommandHandlerArchitectureTests
{
    private static readonly Type[] Handlers =
    [
        typeof(CreateOrganizationHandler),
        typeof(CreatePolicyHandler),
        typeof(DeleteOrganizationHandler),
        typeof(DeletePolicyHandler),
        typeof(CreateAccountHandler),
        typeof(DeleteAccountHandler),
        typeof(CreateClearanceHandler),
        typeof(DeleteClearanceHandler),
        typeof(AppointOrganizationAdminHandler),
        typeof(AddMemberHandler)
    ];

    [Fact]
    public void AdministrationCommandHandlers_ShouldNotDependOnInfrastructureOrEntityFramework()
    {
        var selection = SelectHandlers();

        ArchitectureAssert.NotEmpty(selection, "the Administration command handlers");
        Assert.Equal(Handlers.Length, selection.GetTypes().Count());

        var result = selection
            .ShouldNot().HaveDependencyOnAny(InfrastructureNamespace, EntityFrameworkNamespace)
            .GetResult();

        ArchitectureAssert.Passed(result, "Administration command handlers must not depend on Infrastructure or EF Core");
    }

    private static PredicateList SelectHandlers() =>
        Handlers.Skip(1).Aggregate(
            Types.InAssembly(FeaturesAssembly).That().HaveName(Handlers[0].Name),
            (selection, handler) => selection.Or().HaveName(handler.Name));
}
