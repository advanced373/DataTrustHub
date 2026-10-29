using DataTrustHub.Domain.Memberships;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Features.Administration.Authorization;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Features.Administration.ListMembers;

public record ListMembersQuery(Guid OrganizationId, Guid CallerId);

public class ListMembersHandler(IMembershipRepository memberships, DContext db)
    : IQueryHandler<ListMembersQuery, Result<IReadOnlyList<MemberSummary>>>
{
    private readonly IMembershipRepository _memberships = memberships;
    private readonly DContext _db = db;

    public async Task<Result<IReadOnlyList<MemberSummary>>> Handle(
        ListMembersQuery query,
        CancellationToken cancellationToken)
    {
        if (!await _memberships.IsOrgAdminAsync(query.CallerId, query.OrganizationId, cancellationToken))
            return Result.Failure<IReadOnlyList<MemberSummary>>(Errors.OrganizationNotFound);

        var members = await _db.Memberships
            .AsNoTracking()
            .Where(m => m.OrganizationId == query.OrganizationId)
            .Join(_db.Users, m => m.UserId, u => u.Id, (m, u) => new { Membership = m, u.Email })
            .OrderBy(row => row.Email)
            .Select(row => new MemberSummary(row.Membership.Id, row.Membership.UserId, row.Email, row.Membership.Role))
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<MemberSummary>>(members);
    }
}
