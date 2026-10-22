using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Organizations;
using DataTrustHub.Domain.Policies;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.DeleteGroup;

public record DeleteGroupCommand(Guid GroupId);

public class DeleteGroupHandler(
    IOrganizationRepository organizations,
    IPolicyRepository policies,
    IUnitOfWork unitOfWork)
    : ICommandHandler<DeleteGroupCommand, Result<Guid>>
{
    private readonly IOrganizationRepository _organizations = organizations;
    private readonly IPolicyRepository _policies = policies;
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    // Policies reference groups by id without a foreign key, so the check is done here
    public async Task<Result<Guid>> Handle(
        DeleteGroupCommand command,
        CancellationToken cancellationToken)
    {
        var group = await _organizations.GetByIdAsync(command.GroupId, cancellationToken);
        if (group is null) return Result.Failure<Guid>(Errors.GroupNotFound);

        var hasPolicies = await _policies.AnyInOrganizationAsync(command.GroupId, cancellationToken);
        if (hasPolicies) return Result.Failure<Guid>(Errors.GroupHasPolicies);

        await _organizations.RemoveAsync(group, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(command.GroupId);
    }
}
