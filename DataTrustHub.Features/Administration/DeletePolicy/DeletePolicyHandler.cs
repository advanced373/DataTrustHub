using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Clearances;
using DataTrustHub.Domain.Memberships;
using DataTrustHub.Domain.Policies;
using DataTrustHub.Features._Shared.Auditing;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Features.Administration.Authorization;
using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.DeletePolicy;

public record DeletePolicyCommand(Guid OrganizationId, Guid CallerId, Guid PolicyId)
    : IOrganizationScopedCommand, ITargetedCommand
{
    AuditTarget ITargetedCommand.Target => new(AuditTargetTypes.Policy, PolicyId);
}

// An organization admin deletes a policy of their organization (C6)
public class DeletePolicyHandler(
    IMembershipRepository memberships,
    IPolicyRepository policies,
    IClearanceRepository clearances,
    IUnitOfWork unitOfWork)
    : ICommandHandler<DeletePolicyCommand, Result<Guid>>
{
    private readonly IMembershipRepository _memberships = memberships;
    private readonly IPolicyRepository _policies = policies;
    private readonly IClearanceRepository _clearances = clearances;
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    // Clearances reference policies by id without a foreign key, so the check is done here
    public async Task<Result<Guid>> Handle(
        DeletePolicyCommand command,
        CancellationToken cancellationToken)
    {
        if (!await _memberships.IsOrgAdminAsync(command.CallerId, command.OrganizationId, cancellationToken))
            return Result.Failure<Guid>(Errors.OrganizationNotFound);

        // A policy of another organization is not found, so its existence is not revealed
        var policy = await _policies.GetByIdAsync(command.PolicyId, cancellationToken);
        if (policy is null || policy.OrganizationId != command.OrganizationId)
            return Result.Failure<Guid>(Errors.PolicyNotFound);

        // Invariant P3
        var hasClearances = await _clearances.AnyForPolicyAsync(command.PolicyId, cancellationToken);
        if (hasClearances) return Result.Failure<Guid>(Errors.PolicyHasClearances);

        await _policies.RemoveAsync(policy, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(command.PolicyId);
    }
}
