using DataTrustHub.Features.Administration.CreateGroup;
using DataTrustHub.Features.Administration.CreatePolicy;
using DataTrustHub.Features.Administration.DeleteGroup;
using DataTrustHub.Features.Administration.DeletePolicy;
using NetArchTest.Rules;
using Xunit;
using static DataTrustHub.Features.Tests.Architecture.ArchitectureConstants;

namespace DataTrustHub.Features.Tests.Architecture;

// ADR-0012 step 3b-1: the group and policy command handlers depend on repository interfaces only
public class GroupPolicyCommandHandlerArchitectureTests
{
    private static readonly Type[] Handlers =
    [
        typeof(CreateGroupHandler),
        typeof(CreatePolicyHandler),
        typeof(DeleteGroupHandler),
        typeof(DeletePolicyHandler)
    ];

    [Fact]
    public void GroupPolicyCommandHandlers_ShouldNotDependOnInfrastructureOrEntityFramework()
    {
        var selection = SelectHandlers();

        ArchitectureAssert.NotEmpty(selection, "the group and policy command handlers");
        Assert.Equal(Handlers.Length, selection.GetTypes().Count());

        var result = selection
            .ShouldNot().HaveDependencyOnAny(InfrastructureNamespace, EntityFrameworkNamespace)
            .GetResult();

        ArchitectureAssert.Passed(result, "Group and policy command handlers must not depend on Infrastructure or EF Core");
    }

    private static PredicateList SelectHandlers() =>
        Types.InAssembly(FeaturesAssembly)
            .That().HaveName(Handlers[0].Name)
            .Or().HaveName(Handlers[1].Name)
            .Or().HaveName(Handlers[2].Name)
            .Or().HaveName(Handlers[3].Name);
}
