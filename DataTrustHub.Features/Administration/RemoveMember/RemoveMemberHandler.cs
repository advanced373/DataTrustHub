using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Clearances;
using DataTrustHub.Domain.Memberships;
using DataTrustHub.Domain.Policies;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Features.Administration.Authorization;
using DataTrustHub.Features.Administration.MembershipRules;
using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.RemoveMember;

public record RemoveMemberCommand(Guid OrganizationId, Guid CallerId, Guid UserId);

// An organization admin ends a member's or guest's membership, and with it the user's clearances on the
// organization's policies (C7), in one save. The last organization admin cannot be removed (M2)
public class RemoveMemberHandler(
    IMembershipRepository memberships,
    IPolicyRepository policies,
    IClearanceRepository clearances,
    IUnitOfWork unitOfWork)
    : ICommandHandler<RemoveMemberCommand, Result<Guid>>
{
    private readonly IMembershipRepository _memberships = memberships;
    private readonly IPolicyRepository _policies = policies;
    private readonly IClearanceRepository _clearances = clearances;
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    public async Task<Result<Guid>> Handle(
        RemoveMemberCommand command,
        CancellationToken cancellationToken)
    {
        if (!await _memberships.IsOrgAdminAsync(command.CallerId, command.OrganizationId, cancellationToken))
            return Result.Failure<Guid>(Errors.OrganizationNotFound);

        var membership = await _memberships.GetAsync(command.UserId, command.OrganizationId, cancellationToken);
        if (membership is null) return Result.Failure<Guid>(Errors.MembershipNotFound);

        // Also when an organization admin removes themselves as the last one
        if (await _memberships.IsLastOrgAdminAsync(membership, cancellationToken))
            return Result.Failure<Guid>(Errors.LastOrgAdmin);

        await _clearances.RevokeInOrganizationAsync(_policies, membership.UserId, membership.OrganizationId, cancellationToken);
        await _memberships.RemoveAsync(membership, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(membership.Id);
    }
}
