using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Organizations;
using DataTrustHub.Domain.Policies;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.CreatePolicy;

public record CreatePolicyCommand(string Name, Guid OrganizationId);

public class CreatePolicyHandler(
    IOrganizationRepository organizations,
    IPolicyRepository policies,
    IUnitOfWork unitOfWork)
    : ICommandHandler<CreatePolicyCommand, Result<Guid>>
{
    private readonly IOrganizationRepository _organizations = organizations;
    private readonly IPolicyRepository _policies = policies;
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    public async Task<Result<Guid>> Handle(
        CreatePolicyCommand command,
        CancellationToken cancellationToken)
    {
        var organizationExists = await _organizations.ExistsAsync(command.OrganizationId, cancellationToken);
        if (!organizationExists) return Result.Failure<Guid>(Errors.OrganizationNotFound);

        var nameExists = await _policies.NameExistsInOrganizationAsync(
            command.Name, command.OrganizationId, cancellationToken);
        if (nameExists) return Result.Failure<Guid>(Errors.PolicyNameAlreadyInUse);

        var policy = Policy.Create(command.Name, command.OrganizationId);
        _policies.Add(policy);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(policy.Id);
    }
}
