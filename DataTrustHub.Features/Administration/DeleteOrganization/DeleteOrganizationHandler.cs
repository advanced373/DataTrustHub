using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Organizations;
using DataTrustHub.Domain.Policies;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.DeleteOrganization;

public record DeleteOrganizationCommand(Guid OrganizationId);

public class DeleteOrganizationHandler(
    IOrganizationRepository organizations,
    IPolicyRepository policies,
    IUnitOfWork unitOfWork)
    : ICommandHandler<DeleteOrganizationCommand, Result<Guid>>
{
    private readonly IOrganizationRepository _organizations = organizations;
    private readonly IPolicyRepository _policies = policies;
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    // Policies reference organizations by id without a foreign key, so the check is done here
    public async Task<Result<Guid>> Handle(
        DeleteOrganizationCommand command,
        CancellationToken cancellationToken)
    {
        var organization = await _organizations.GetByIdAsync(command.OrganizationId, cancellationToken);
        if (organization is null) return Result.Failure<Guid>(Errors.OrganizationNotFound);

        var hasPolicies = await _policies.AnyInOrganizationAsync(command.OrganizationId, cancellationToken);
        if (hasPolicies) return Result.Failure<Guid>(Errors.OrganizationHasPolicies);

        await _organizations.RemoveAsync(organization, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(command.OrganizationId);
    }
}
