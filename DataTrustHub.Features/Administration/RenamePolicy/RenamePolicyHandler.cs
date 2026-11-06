using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Memberships;
using DataTrustHub.Domain.Policies;
using DataTrustHub.Features._Shared.Auditing;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Features.Administration.Authorization;
using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.RenamePolicy;

public record RenamePolicyCommand(Guid OrganizationId, Guid CallerId, Guid PolicyId, string Name)
    : IOrganizationScopedCommand, ITargetedCommand
{
    AuditTarget ITargetedCommand.Target => new(AuditTargetTypes.Policy, PolicyId);
}

// An organization admin renames a policy of their organization (C6). Its id, organization and clearances
// stay as they are, so data items classified under it stay readable by the same users
public class RenamePolicyHandler(
    IMembershipRepository memberships,
    IPolicyRepository policies,
    IUnitOfWork unitOfWork)
    : ICommandHandler<RenamePolicyCommand, Result<Guid>>
{
    private readonly IMembershipRepository _memberships = memberships;
    private readonly IPolicyRepository _policies = policies;
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    public async Task<Result<Guid>> Handle(
        RenamePolicyCommand command,
        CancellationToken cancellationToken)
    {
        if (!await _memberships.IsOrgAdminAsync(command.CallerId, command.OrganizationId, cancellationToken))
            return Result.Failure<Guid>(Errors.OrganizationNotFound);

        // A policy of another organization is not found, so its existence is not revealed
        var policy = await _policies.GetByIdAsync(command.PolicyId, cancellationToken);
        if (policy is null || policy.OrganizationId != command.OrganizationId)
            return Result.Failure<Guid>(Errors.PolicyNotFound);

        // Exactly its current name: nothing to change
        if (policy.Name == command.Name) return Result.Success(policy.Id);

        // P2 looks only at the organization's other policies: a new letter case or spacing of its own name
        // is no conflict
        var nameExists = await _policies.NameExistsForOtherInOrganizationAsync(
            command.Name, command.OrganizationId, policy.Id, cancellationToken);
        if (nameExists) return Result.Failure<Guid>(Errors.PolicyNameAlreadyInUse);

        policy.Rename(command.Name);
        await _policies.UpdateAsync(policy, cancellationToken);
        return await SavePolicy(policy, cancellationToken);
    }

    // P2 under concurrency: a request that passed NameExistsForOtherInOrganizationAsync at the same time saved
    // first, and the unique index rejects this one
    private async Task<Result<Guid>> SavePolicy(Policy policy, CancellationToken cancellationToken)
    {
        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success(policy.Id);
        }
        catch (UniqueConstraintViolationException)
        {
            return Result.Failure<Guid>(Errors.PolicyNameAlreadyInUse);
        }
    }
}
