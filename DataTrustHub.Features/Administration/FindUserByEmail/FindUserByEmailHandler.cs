using DataTrustHub.Domain.Memberships;
using DataTrustHub.Domain.Users;
using DataTrustHub.Features._Shared.Auditing;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Features.Administration.Authorization;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Features.Administration.FindUserByEmail;

// Every lookup is audited (ADR-0025): the record names the organization, and the user when found,
// never the email address searched for
public record FindUserByEmailQuery(Guid OrganizationId, Guid CallerId, string Email)
    : IAuditedQuery, IOrganizationScopedQuery;

// An organization admin finds a user by their exact email address, to add them as a member or guest
// (ADR-0014). Exact match only, so an organization admin can confirm an address they already know but
// cannot list or search the users of the platform
public class FindUserByEmailHandler(IMembershipRepository memberships, DContext db, AuditScope auditScope)
    : IQueryHandler<FindUserByEmailQuery, Result<FindUserByEmailResponse>>
{
    private readonly IMembershipRepository _memberships = memberships;
    private readonly DContext _db = db;
    private readonly AuditScope _auditScope = auditScope;

    public async Task<Result<FindUserByEmailResponse>> Handle(
        FindUserByEmailQuery query,
        CancellationToken cancellationToken)
    {
        if (!await _memberships.IsOrgAdminAsync(query.CallerId, query.OrganizationId, cancellationToken))
            return Result.Failure<FindUserByEmailResponse>(Errors.OrganizationNotFound);

        // The same comparison as log in (IUserRepository.GetByEmailAsync): the normalized address (U7),
        // by equality in the database
        var normalizedEmail = EmailAddress.From(query.Email).Value;
        var user = await _db.Users
            .AsNoTracking()
            .Where(u => u.Email == normalizedEmail)
            .Select(u => new { u.Id, u.Email })
            .FirstOrDefaultAsync(cancellationToken);
        if (user is null) return Result.Failure<FindUserByEmailResponse>(Errors.UserNotFound);

        _auditScope.SetTarget(new AuditTarget(AuditTargetTypes.User, user.Id));

        var role = await FindMembershipRoleAsync(user.Id, query.OrganizationId, cancellationToken);
        return Result.Success(new FindUserByEmailResponse(user.Id, user.Email, role is not null, role));
    }

    private Task<string?> FindMembershipRoleAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken) =>
        _db.Memberships
            .AsNoTracking()
            .Where(m => m.UserId == userId && m.OrganizationId == organizationId)
            .Select(m => m.Role)
            .FirstOrDefaultAsync(cancellationToken);
}
