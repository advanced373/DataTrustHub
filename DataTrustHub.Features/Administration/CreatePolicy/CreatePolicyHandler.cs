using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Memberships;
using DataTrustHub.Domain.Policies;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Features.Administration.Authorization;
using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.CreatePolicy;

public record CreatePolicyCommand(Guid OrganizationId, Guid CallerId, string Name);

// An organization admin creates a policy of their organization (C6)
public class CreatePolicyHandler(
    IMembershipRepository memberships,
    IPolicyRepository policies,
    IUnitOfWork unitOfWork)
    : ICommandHandler<CreatePolicyCommand, Result<Guid>>
{
    private readonly IMembershipRepository _memberships = memberships;
    private readonly IPolicyRepository _policies = policies;
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    public async Task<Result<Guid>> Handle(
        CreatePolicyCommand command,
        CancellationToken cancellationToken)
    {
        // Also P1: an organization admin's membership exists only in an existing organization (O3)
        if (!await _memberships.IsOrgAdminAsync(command.CallerId, command.OrganizationId, cancellationToken))
            return Result.Failure<Guid>(Errors.OrganizationNotFound);

        // Invariant P2
        var nameExists = await _policies.NameExistsInOrganizationAsync(
            command.Name, command.OrganizationId, cancellationToken);
        if (nameExists) return Result.Failure<Guid>(Errors.PolicyNameAlreadyInUse);

        var policy = Policy.Create(command.Name, command.OrganizationId);
        _policies.Add(policy);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(policy.Id);
    }
}
