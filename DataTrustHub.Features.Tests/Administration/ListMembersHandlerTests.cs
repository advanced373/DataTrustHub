using DataTrustHub.Domain.Memberships;
using DataTrustHub.Domain.ReadModels.Memberships;
using DataTrustHub.Features.Administration.ListMembers;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.SharedKernel;
using Xunit;

namespace DataTrustHub.Features.Tests.Administration;

// #74 part 4b, AC4: the members list keeps its authorization on the membership repository, checked before the read
// side is asked for anything. Built directly with fakes, so no database is involved
public class ListMembersHandlerTests
{
    private const string MemberEmail = "list_members_handler@example.com";

    private static readonly Guid OrganizationId = Guid.NewGuid();
    private static readonly Guid OtherOrganizationId = Guid.NewGuid();

    private readonly FakeMembershipRepository _memberships = new();
    private readonly RecordingMembershipQueries _membershipQueries = new();

    [Fact]
    public async Task ListMembers_CallerWithoutMembership_FailsWithOrganizationNotFoundWithoutReading()
    {
        var result = await Handler().Handle(new ListMembersQuery(OrganizationId, Guid.NewGuid()), default);

        AssertOrganizationNotFound(result);
        Assert.Equal(0, _membershipQueries.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ListMembers_CallerIsMemberOrGuest_FailsWithOrganizationNotFoundWithoutReading(bool guest)
    {
        var caller = _memberships.Seed(guest
            ? Membership.AddGuest(Guid.NewGuid(), OrganizationId)
            : Membership.AddMember(Guid.NewGuid(), OrganizationId));

        var result = await Handler().Handle(new ListMembersQuery(OrganizationId, caller.UserId), default);

        AssertOrganizationNotFound(result);
        Assert.Equal(0, _membershipQueries.Calls);
    }

    [Fact]
    public async Task ListMembers_OrgAdminOfAnotherOrganization_FailsWithOrganizationNotFoundWithoutReading()
    {
        var caller = _memberships.Seed(Membership.AppointOrgAdmin(Guid.NewGuid(), OtherOrganizationId));

        var result = await Handler().Handle(new ListMembersQuery(OrganizationId, caller.UserId), default);

        AssertOrganizationNotFound(result);
        Assert.Equal(0, _membershipQueries.Calls);
    }

    [Fact]
    public async Task ListMembers_OrgAdmin_ReturnsTheReadSideMembersInTheirOrder()
    {
        var caller = _memberships.Seed(Membership.AppointOrgAdmin(Guid.NewGuid(), OrganizationId));
        var member = new OrganizationMember(Guid.NewGuid(), Guid.NewGuid(), MemberEmail, MembershipRole.Member.Name);
        _membershipQueries.Members = [member];

        var result = await Handler().Handle(new ListMembersQuery(OrganizationId, caller.UserId), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            [new MemberSummary(member.MembershipId, member.UserId, member.Email, member.Role)],
            result.Value);
        Assert.Equal(OrganizationId, Assert.Single(_membershipQueries.RequestedOrganizationIds));
    }

    private ListMembersHandler Handler() => new(_memberships, _membershipQueries);

    private static void AssertOrganizationNotFound(Result<IReadOnlyList<MemberSummary>> result)
    {
        Assert.True(result.IsFailure);
        Assert.Equal((AdminErrorCodes.OrganizationNotFound, ErrorType.NotFound), (result.Error.Code, result.Error.Type));
    }

    // Records every read, so a test can prove the handler did not read before its authorization check
    private sealed class RecordingMembershipQueries : IMembershipQueries
    {
        internal IReadOnlyList<OrganizationMember> Members { get; set; } = [];
        internal List<Guid> RequestedOrganizationIds { get; } = [];
        internal int Calls { get; private set; }

        public Task<IReadOnlyList<OrganizationMember>> ListMembersAsync(Guid organizationId, CancellationToken cancellationToken)
        {
            Calls++;
            RequestedOrganizationIds.Add(organizationId);
            return Task.FromResult(Members);
        }

        public Task<IReadOnlyList<MyOrganization>> ListMyOrganizationsAsync(Guid userId, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<MyOrganization>>([]);
        }
    }
}
