using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.DataItems;
using DataTrustHub.Domain.Memberships;
using DataTrustHub.Domain.Policies;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.DataManagement.DeleteDataItem;

public record DeleteDataItemCommand(Guid DataItemId, Guid RequesterId);

public class DeleteDataItemHandler(
    IDataItemRepository dataItems,
    IPolicyRepository policies,
    IMembershipRepository memberships,
    IUnitOfWork unitOfWork)
    : ICommandHandler<DeleteDataItemCommand, Result<Guid>>
{
    private readonly IDataItemRepository _dataItems = dataItems;
    private readonly IPolicyRepository _policies = policies;
    private readonly IMembershipRepository _memberships = memberships;
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    public async Task<Result<Guid>> Handle(
        DeleteDataItemCommand command,
        CancellationToken cancellationToken)
    {
        var dataItem = await _dataItems.GetByIdAsync(command.DataItemId, cancellationToken);
        if (dataItem is null)
            return Result.Failure<Guid>(DataItemErrors.NotFound);

        // The entity applies D5' and hides items the requester may not delete behind "not found"
        var rights = await GatherDeletionRights(dataItem, command.RequesterId, cancellationToken);
        var deleted = dataItem.MarkDeleted(command.RequesterId, rights);
        if (deleted.IsFailure)
            return Result.Failure<Guid>(deleted.Error);

        await _dataItems.UpdateAsync(dataItem, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(command.DataItemId);
    }

    // A classified item's organization is its policy's; a policy deleted under its items leaves none
    private async Task<DeletionRights> GatherDeletionRights(DataItem dataItem, Guid requesterId, CancellationToken ct)
    {
        if (dataItem.PolicyId is not Guid policyId) return DeletionRights.WithoutOrganization;

        var policy = await _policies.GetByIdAsync(policyId, ct);
        if (policy is null) return DeletionRights.WithoutOrganization;

        var membership = await _memberships.GetAsync(requesterId, policy.OrganizationId, ct);
        return DeletionRights.InOrganization(membership);
    }
}
