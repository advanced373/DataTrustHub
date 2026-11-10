using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Organizations;
using DataTrustHub.Features._Shared.Auditing;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.RenameOrganization;

public record RenameOrganizationCommand(Guid OrganizationId, string Name)
    : IOrganizationScopedCommand, ITargetedCommand
{
    AuditTarget ITargetedCommand.Target => new(AuditTargetTypes.Organization, OrganizationId);
}

// The platform admin renames an organization (ADR-0014). Its id, memberships and policies stay as they are
public class RenameOrganizationHandler(IOrganizationRepository organizations, IUnitOfWork unitOfWork)
    : ICommandHandler<RenameOrganizationCommand, Result<Guid>>
{
    private readonly IOrganizationRepository _organizations = organizations;
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    public async Task<Result<Guid>> Handle(
        RenameOrganizationCommand command,
        CancellationToken cancellationToken)
    {
        var organization = await _organizations.GetByIdAsync(command.OrganizationId, cancellationToken);
        if (organization is null) return Result.Failure<Guid>(Errors.OrganizationNotFound);

        // Exactly its current name: nothing to change
        if (organization.Name == command.Name) return Result.Success(organization.Id);

        // O1 looks only at the other organizations: a new letter case or spacing of its own name is no conflict
        var nameExists = await _organizations.NameExistsForOtherAsync(
            command.Name, organization.Id, cancellationToken);
        if (nameExists) return Result.Failure<Guid>(Errors.OrganizationNameAlreadyInUse);

        organization.Rename(command.Name);
        await _organizations.UpdateAsync(organization, cancellationToken);
        return await SaveOrganization(organization, cancellationToken);
    }

    // O1 under concurrency: a request that passed NameExistsForOtherAsync at the same time saved first, and the
    // unique index rejects this one
    private async Task<Result<Guid>> SaveOrganization(Organization organization, CancellationToken cancellationToken)
    {
        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success(organization.Id);
        }
        catch (UniqueConstraintViolationException)
        {
            return Result.Failure<Guid>(Errors.OrganizationNameAlreadyInUse);
        }
    }
}
