using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Organizations;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.CreateOrganization;

public record CreateOrganizationCommand(string Name);

public class CreateOrganizationHandler(IOrganizationRepository organizations, IUnitOfWork unitOfWork)
    : ICommandHandler<CreateOrganizationCommand, Result<Guid>>
{
    private readonly IOrganizationRepository _organizations = organizations;
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    public async Task<Result<Guid>> Handle(
        CreateOrganizationCommand command,
        CancellationToken cancellationToken)
    {
        var nameExists = await _organizations.NameExistsAsync(command.Name, cancellationToken);
        if (nameExists) return Result.Failure<Guid>(Errors.OrganizationNameAlreadyInUse);

        var organization = Organization.Create(command.Name);
        _organizations.Add(organization);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(organization.Id);
    }
}
