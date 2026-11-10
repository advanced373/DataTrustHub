using DataTrustHub.Domain.Memberships;
using DataTrustHub.Domain.ReadModels.Memberships;
using DataTrustHub.Domain.ReadModels.Registrations;
using DataTrustHub.Domain.ReadModels.Users;
using DataTrustHub.Domain.Registrations;
using DataTrustHub.Features._Shared.Auditing;
using DataTrustHub.Features._Shared.Registration;
using DataTrustHub.Features.Administration.FindUserByEmail;
using DataTrustHub.Features.Administration.GetAccount;
using DataTrustHub.Features.Administration.ListAccounts;
using DataTrustHub.Features.Administration.ListRegistrationRequests;
using Microsoft.Extensions.Options;
using Xunit;

namespace DataTrustHub.Features.Tests.Architecture;

// #74 (ADR-0012 step 4, part 4d): the account, user lookup and registration request queries of Administration read
// through the read-side ports: the exact constructor dependencies of the four handlers. The layering rule
// (FeaturesPersistenceIsolationTests) and the ports and read models, the password hash rule included
// (ReadSideArchitectureTests), are checked project-wide (#74 step 5)
public class AccountReadSideArchitectureTests
{
    [Fact]
    public void GetAccountHandler_DependsOnlyOnTheUserQueries()
    {
        Assert.Equal(new HashSet<Type> { typeof(IUserQueries) }, ConstructorDependencies(typeof(GetAccountHandler)));
    }

    [Fact]
    public void ListAccountsHandler_DependsOnlyOnTheUserQueries()
    {
        Assert.Equal(new HashSet<Type> { typeof(IUserQueries) }, ConstructorDependencies(typeof(ListAccountsHandler)));
    }

    // The authorization check stays on the membership repository and the target on the audit scope (AC4); the user
    // and their role come from the read side
    [Fact]
    public void FindUserByEmailHandler_DependsOnTheMembershipRepositoryTheReadSideAndTheAuditScope()
    {
        Assert.Equal(
            new HashSet<Type>
            {
                typeof(IMembershipRepository), typeof(IUserQueries), typeof(IMembershipQueries), typeof(AuditScope)
            },
            ConstructorDependencies(typeof(FindUserByEmailHandler)));
    }

    // The purge stays on the repository (R2, AC4); only the read of the pending requests comes from the read side
    [Fact]
    public void ListRegistrationRequestsHandler_DependsOnTheRepositoryTheReadSideTheOptionsAndTheClock()
    {
        Assert.Equal(
            new HashSet<Type>
            {
                typeof(IRegistrationRequestRepository), typeof(IRegistrationRequestQueries),
                typeof(IOptions<RegistrationOptions>), typeof(TimeProvider)
            },
            ConstructorDependencies(typeof(ListRegistrationRequestsHandler)));
    }

    private static HashSet<Type> ConstructorDependencies(Type handler) =>
        handler.GetConstructors().Single().GetParameters().Select(p => p.ParameterType).ToHashSet();
}
