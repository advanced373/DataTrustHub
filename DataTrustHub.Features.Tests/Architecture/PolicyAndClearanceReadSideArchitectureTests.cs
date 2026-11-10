using DataTrustHub.Domain.Memberships;
using DataTrustHub.Domain.ReadModels.Clearances;
using DataTrustHub.Domain.ReadModels.Policies;
using DataTrustHub.Features.Administration.GetClearance;
using DataTrustHub.Features.Administration.GetPolicy;
using DataTrustHub.Features.Administration.ListClearances;
using DataTrustHub.Features.Administration.ListPolicies;
using Xunit;

namespace DataTrustHub.Features.Tests.Architecture;

// #74 (ADR-0012 step 4, part 4c): the policy and clearance queries of Administration read through the read-side
// ports: the exact constructor dependencies of the four handlers. The layering rule (FeaturesPersistenceIsolationTests)
// and the ports and read models (ReadSideArchitectureTests) are checked project-wide (#74 step 5)
public class PolicyAndClearanceReadSideArchitectureTests
{
    // The authorization check stays on the membership repository (AC4); the policies come from the read side
    [Fact]
    public void GetPolicyHandler_DependsOnlyOnTheMembershipRepositoryAndThePolicyQueries()
    {
        Assert.Equal(
            new HashSet<Type> { typeof(IMembershipRepository), typeof(IPolicyQueries) },
            ConstructorDependencies(typeof(GetPolicyHandler)));
    }

    [Fact]
    public void ListPoliciesHandler_DependsOnlyOnTheMembershipRepositoryAndThePolicyQueries()
    {
        Assert.Equal(
            new HashSet<Type> { typeof(IMembershipRepository), typeof(IPolicyQueries) },
            ConstructorDependencies(typeof(ListPoliciesHandler)));
    }

    // The authorization check stays on the membership repository (AC4); the clearances come from the read side
    [Fact]
    public void GetClearanceHandler_DependsOnlyOnTheMembershipRepositoryAndTheClearanceQueries()
    {
        Assert.Equal(
            new HashSet<Type> { typeof(IMembershipRepository), typeof(IClearanceQueries) },
            ConstructorDependencies(typeof(GetClearanceHandler)));
    }

    [Fact]
    public void ListClearancesHandler_DependsOnlyOnTheMembershipRepositoryAndTheClearanceQueries()
    {
        Assert.Equal(
            new HashSet<Type> { typeof(IMembershipRepository), typeof(IClearanceQueries) },
            ConstructorDependencies(typeof(ListClearancesHandler)));
    }

    private static HashSet<Type> ConstructorDependencies(Type handler) =>
        handler.GetConstructors().Single().GetParameters().Select(p => p.ParameterType).ToHashSet();
}
