using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Clearances;
using DataTrustHub.Domain.Memberships;
using DataTrustHub.Domain.Policies;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Features.Administration.Authorization;
using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.DeleteClearance;

public record DeleteClearanceCommand(Guid OrganizationId, Guid CallerId, Guid ClearanceId);

// An organization admin revokes a clearance on a policy of their organization (C6). Revoking their own
// clearance is allowed: it only removes access (ADR-0014)
public class DeleteClearanceHandler(
    IMembershipRepository memberships,
    IPolicyRepository policies,
    IClearanceRepository clearances,
    IUnitOfWork unitOfWork)
    : ICommandHandler<DeleteClearanceCommand, Result<Guid>>
{
    private readonly IMembershipRepository _memberships = memberships;
    private readonly IPolicyRepository _policies = policies;
    private readonly IClearanceRepository _clearances = clearances;
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    // The classification level stays: other clearances may share it
    public async Task<Result<Guid>> Handle(
        DeleteClearanceCommand command,
        CancellationToken cancellationToken)
    {
        if (!await _memberships.IsOrgAdminAsync(command.CallerId, command.OrganizationId, cancellationToken))
            return Result.Failure<Guid>(Errors.OrganizationNotFound);

        // A clearance on a policy of another organization is not found, so its existence is not revealed
        var clearance = await _clearances.GetByIdAsync(command.ClearanceId, cancellationToken);
        if (clearance is null || !await IsInOrganizationAsync(clearance, command.OrganizationId, cancellationToken))
            return Result.Failure<Guid>(Errors.ClearanceNotFound);

        await _clearances.RemoveAsync(clearance, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(command.ClearanceId);
    }

    // True when the clearance's policy belongs to the organization
    private async Task<bool> IsInOrganizationAsync(
        Clearance clearance,
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        var policy = await _policies.GetByIdAsync(clearance.PolicyId, cancellationToken);
        return policy is not null && policy.OrganizationId == organizationId;
    }
}
