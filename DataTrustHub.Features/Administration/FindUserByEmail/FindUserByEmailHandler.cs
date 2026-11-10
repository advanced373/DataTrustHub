using DataTrustHub.Domain.Memberships;
using DataTrustHub.Domain.ReadModels.Memberships;
using DataTrustHub.Domain.ReadModels.Users;
using DataTrustHub.Domain.Users;
using DataTrustHub.Features._Shared.Auditing;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Features.Administration.Authorization;
using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.FindUserByEmail;

// Every lookup is audited (ADR-0025): the record names the organization, and the user when found,
// never the email address searched for
public record FindUserByEmailQuery(Guid OrganizationId, Guid CallerId, string Email)
    : IAuditedQuery, IOrganizationScopedQuery;

// An organization admin finds a user by their exact email address, to add them as a member or guest
// (ADR-0014). Exact match only, so an organization admin can confirm an address they already know but
// cannot list or search the users of the platform
public class FindUserByEmailHandler(
    IMembershipRepository memberships,
    IUserQueries userQueries,
    IMembershipQueries membershipQueries,
    AuditScope auditScope)
    : IQueryHandler<FindUserByEmailQuery, Result<FindUserByEmailResponse>>
{
    private readonly IMembershipRepository _memberships = memberships;
    private readonly IUserQueries _userQueries = userQueries;
    private readonly IMembershipQueries _membershipQueries = membershipQueries;
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
        var user = await _userQueries.FindByEmailAsync(normalizedEmail, cancellationToken);
        if (user is null) return Result.Failure<FindUserByEmailResponse>(Errors.UserNotFound);

        _auditScope.SetTarget(new AuditTarget(AuditTargetTypes.User, user.Id));

        var role = await _membershipQueries.FindRoleAsync(user.Id, query.OrganizationId, cancellationToken);
        return Result.Success(new FindUserByEmailResponse(user.Id, user.Email, role is not null, role));
    }
}
