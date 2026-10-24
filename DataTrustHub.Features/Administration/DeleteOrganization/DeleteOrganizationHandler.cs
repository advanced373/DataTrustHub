using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Memberships;
using DataTrustHub.Domain.Organizations;
using DataTrustHub.Domain.Policies;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.DeleteOrganization;

public record DeleteOrganizationCommand(Guid OrganizationId);

public class DeleteOrganizationHandler(
    IOrganizationRepository organizations,
    IPolicyRepository policies,
    IMembershipRepository memberships,
    IUnitOfWork unitOfWork)
    : ICommandHandler<DeleteOrganizationCommand, Result<Guid>>
{
    private readonly IOrganizationRepository _organizations = organizations;
    private readonly IPolicyRepository _policies = policies;
    private readonly IMembershipRepository _memberships = memberships;
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    // Policies reference organizations by id without a foreign key, so the check is done here. The
    // organization's memberships end with it, in the same save; with no policies left, no clearances do (C7)
    public async Task<Result<Guid>> Handle(
        DeleteOrganizationCommand command,
        CancellationToken cancellationToken)
    {
        var organization = await _organizations.GetByIdAsync(command.OrganizationId, cancellationToken);
        if (organization is null) return Result.Failure<Guid>(Errors.OrganizationNotFound);

        var hasPolicies = await _policies.AnyInOrganizationAsync(command.OrganizationId, cancellationToken);
        if (hasPolicies) return Result.Failure<Guid>(Errors.OrganizationHasPolicies);

        await RemoveMemberships(command.OrganizationId, cancellationToken);
        await _organizations.RemoveAsync(organization, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(command.OrganizationId);
    }

    private async Task RemoveMemberships(Guid organizationId, CancellationToken ct)
    {
        var organizationMemberships = await _memberships.ListForOrganizationAsync(organizationId, ct);
        foreach (var membership in organizationMemberships)
            await _memberships.RemoveAsync(membership, ct);
    }
}
