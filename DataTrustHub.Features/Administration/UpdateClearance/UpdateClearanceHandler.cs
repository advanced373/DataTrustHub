using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Clearances;
using DataTrustHub.Domain.Memberships;
using DataTrustHub.Domain.Policies;
using DataTrustHub.Features._Shared.Auditing;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Features.Administration.Authorization;
using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.UpdateClearance;

public record UpdateClearanceCommand(
    Guid OrganizationId,
    Guid CallerId,
    Guid ClearanceId,
    string Name,
    string ClassificationLevelName,
    int ClassificationLevelPriority)
    : IOrganizationScopedCommand, ITargetedCommand
{
    AuditTarget ITargetedCommand.Target => new(AuditTargetTypes.Clearance, ClearanceId);
}

// An organization admin changes the name and the classification level of a clearance on a policy of their
// organization (C6), never of their own (C8). The user and the policy stay (ADR-0029); the new level applies
// to data access right away (C1)
public class UpdateClearanceHandler(
    IMembershipRepository memberships,
    IPolicyRepository policies,
    IClearanceRepository clearances,
    IUnitOfWork unitOfWork)
    : ICommandHandler<UpdateClearanceCommand, Result<Guid>>
{
    private readonly IMembershipRepository _memberships = memberships;
    private readonly IPolicyRepository _policies = policies;
    private readonly IClearanceRepository _clearances = clearances;
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    public async Task<Result<Guid>> Handle(
        UpdateClearanceCommand command,
        CancellationToken cancellationToken)
    {
        if (!await _memberships.IsOrgAdminAsync(command.CallerId, command.OrganizationId, cancellationToken))
            return Result.Failure<Guid>(Errors.OrganizationNotFound);

        // A clearance on a policy of another organization is not found, so its existence is not revealed
        var clearance = await _clearances.GetByIdAsync(command.ClearanceId, cancellationToken);
        if (clearance is null || !await IsInOrganizationAsync(clearance, command.OrganizationId, cancellationToken))
            return Result.Failure<Guid>(Errors.ClearanceNotFound);

        // Invariant C8: another organization admin of the organization must change it
        if (clearance.UserId == command.CallerId) return Result.Failure<Guid>(Errors.CannotChangeOwn);

        clearance.Change(
            command.Name,
            new ClassificationLevel(command.ClassificationLevelName, command.ClassificationLevelPriority));
        await _clearances.UpdateAsync(clearance, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(clearance.Id);
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
