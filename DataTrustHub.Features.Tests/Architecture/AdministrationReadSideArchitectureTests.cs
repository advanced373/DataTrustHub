using DataTrustHub.Domain.Memberships;
using DataTrustHub.Domain.ReadModels.Memberships;
using DataTrustHub.Domain.ReadModels.Organizations;
using DataTrustHub.Features.Administration.GetOrganization;
using DataTrustHub.Features.Administration.ListMembers;
using DataTrustHub.Features.Administration.ListMyOrganizations;
using DataTrustHub.Features.Administration.ListOrganizations;
using Xunit;

namespace DataTrustHub.Features.Tests.Architecture;

// #74 (ADR-0012 step 4, part 4b): the organization and membership queries of Administration read through the
// read-side ports: the exact constructor dependencies of the four handlers. The layering rule
// (FeaturesPersistenceIsolationTests) and the ports and read models (ReadSideArchitectureTests) are checked
// project-wide (#74 step 5)
public class AdministrationReadSideArchitectureTests
{
    [Fact]
    public void GetOrganizationHandler_DependsOnlyOnTheOrganizationQueries()
    {
        Assert.Equal(new HashSet<Type> { typeof(IOrganizationQueries) }, ConstructorDependencies(typeof(GetOrganizationHandler)));
    }

    [Fact]
    public void ListOrganizationsHandler_DependsOnlyOnTheOrganizationQueries()
    {
        Assert.Equal(new HashSet<Type> { typeof(IOrganizationQueries) }, ConstructorDependencies(typeof(ListOrganizationsHandler)));
    }

    // The authorization check stays on the membership repository (AC4); the members come from the read side
    [Fact]
    public void ListMembersHandler_DependsOnlyOnTheMembershipRepositoryAndTheMembershipQueries()
    {
        Assert.Equal(
            new HashSet<Type> { typeof(IMembershipRepository), typeof(IMembershipQueries) },
            ConstructorDependencies(typeof(ListMembersHandler)));
    }

    [Fact]
    public void ListMyOrganizationsHandler_DependsOnlyOnTheMembershipQueries()
    {
        Assert.Equal(new HashSet<Type> { typeof(IMembershipQueries) }, ConstructorDependencies(typeof(ListMyOrganizationsHandler)));
    }

    private static HashSet<Type> ConstructorDependencies(Type handler) =>
        handler.GetConstructors().Single().GetParameters().Select(p => p.ParameterType).ToHashSet();
}
