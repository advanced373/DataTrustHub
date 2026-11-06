using DataTrustHub.Domain.Memberships;
using DataTrustHub.Domain.ReadModels.Memberships;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Features.Administration.Authorization;
using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.ListMembers;

public record ListMembersQuery(Guid OrganizationId, Guid CallerId);

public class ListMembersHandler(IMembershipRepository memberships, IMembershipQueries membershipQueries)
    : IQueryHandler<ListMembersQuery, Result<IReadOnlyList<MemberSummary>>>
{
    private readonly IMembershipRepository _memberships = memberships;
    private readonly IMembershipQueries _membershipQueries = membershipQueries;

    public async Task<Result<IReadOnlyList<MemberSummary>>> Handle(
        ListMembersQuery query,
        CancellationToken cancellationToken)
    {
        if (!await _memberships.IsOrgAdminAsync(query.CallerId, query.OrganizationId, cancellationToken))
            return Result.Failure<IReadOnlyList<MemberSummary>>(Errors.OrganizationNotFound);

        var members = await _membershipQueries.ListMembersAsync(query.OrganizationId, cancellationToken);
        return Result.Success<IReadOnlyList<MemberSummary>>(members.Select(ToSummary).ToList());
    }

    private static MemberSummary ToSummary(OrganizationMember member) =>
        new(member.MembershipId, member.UserId, member.Email, member.Role);
}
