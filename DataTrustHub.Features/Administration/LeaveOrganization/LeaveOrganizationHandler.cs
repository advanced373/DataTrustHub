using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Clearances;
using DataTrustHub.Domain.Memberships;
using DataTrustHub.Domain.Policies;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Features.Administration.MembershipRules;
using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.LeaveOrganization;

public record LeaveOrganizationCommand(Guid OrganizationId, Guid CallerId);

// A member, guest or organization admin ends their own membership, and with it their clearances on the
// organization's policies (C7), in one save. The last organization admin cannot leave (M2). This is also
// how the platform admins that the AddMemberships migration made organization admins leave (ADR-0014)
public class LeaveOrganizationHandler(
    IMembershipRepository memberships,
    IPolicyRepository policies,
    IClearanceRepository clearances,
    IUnitOfWork unitOfWork)
    : ICommandHandler<LeaveOrganizationCommand, Result<Guid>>
{
    private readonly IMembershipRepository _memberships = memberships;
    private readonly IPolicyRepository _policies = policies;
    private readonly IClearanceRepository _clearances = clearances;
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    public async Task<Result<Guid>> Handle(
        LeaveOrganizationCommand command,
        CancellationToken cancellationToken)
    {
        var membership = await _memberships.GetAsync(command.CallerId, command.OrganizationId, cancellationToken);
        if (membership is null) return Result.Failure<Guid>(Errors.OrganizationNotFound);

        if (await _memberships.IsLastOrgAdminAsync(membership, cancellationToken))
            return Result.Failure<Guid>(Errors.LastOrgAdmin);

        await _clearances.RevokeInOrganizationAsync(_policies, membership.UserId, membership.OrganizationId, cancellationToken);
        await _memberships.RemoveAsync(membership, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(membership.Id);
    }
}
