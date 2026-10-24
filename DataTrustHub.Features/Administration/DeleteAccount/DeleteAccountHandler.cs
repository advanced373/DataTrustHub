using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Clearances;
using DataTrustHub.Domain.DataItems;
using DataTrustHub.Domain.Memberships;
using DataTrustHub.Domain.Users;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.DeleteAccount;

public record DeleteAccountCommand(Guid AccountId, Guid RequesterId);

public class DeleteAccountHandler(
    IUserRepository users,
    IClearanceRepository clearances,
    IDataItemRepository dataItems,
    IMembershipRepository memberships,
    IUnitOfWork unitOfWork)
    : ICommandHandler<DeleteAccountCommand, Result<Guid>>
{
    private readonly IUserRepository _users = users;
    private readonly IClearanceRepository _clearances = clearances;
    private readonly IDataItemRepository _dataItems = dataItems;
    private readonly IMembershipRepository _memberships = memberships;
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    // Removes the user, their clearances and their memberships, and soft-deletes the data they own, in one
    // save. Removing every clearance also covers C7 for each ended membership. M2 does not block it: an
    // organization that loses its last organization admin this way gets one again from the platform admin
    public async Task<Result<Guid>> Handle(
        DeleteAccountCommand command,
        CancellationToken cancellationToken)
    {
        var user = await _users.GetByIdAsync(command.AccountId, cancellationToken);
        if (user is null) return Result.Failure<Guid>(Errors.AccountNotFound);

        var canBeDeleted = user.CheckCanBeDeletedBy(command.RequesterId);
        if (canBeDeleted.IsFailure) return Result.Failure<Guid>(canBeDeleted.Error);

        await RemoveClearances(user.Id, cancellationToken);
        await SoftDeleteOwnedData(user.Id, cancellationToken);
        await RemoveMemberships(user.Id, cancellationToken);
        await _users.RemoveAsync(user, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(command.AccountId);
    }

    private async Task RemoveClearances(Guid userId, CancellationToken ct)
    {
        var userClearances = await _clearances.ListForUserAsync(userId, ct);
        foreach (var clearance in userClearances)
            await _clearances.RemoveAsync(clearance, ct);
    }

    // An orphaned organization admin membership would keep counting for M2 and AlreadyHasOrgAdmin
    private async Task RemoveMemberships(Guid userId, CancellationToken ct)
    {
        var userMemberships = await _memberships.ListForUserAsync(userId, ct);
        foreach (var membership in userMemberships)
            await _memberships.RemoveAsync(membership, ct);
    }

    // Only items MarkDeleted accepts are updated; one already deleted concurrently is skipped, not an error
    private async Task SoftDeleteOwnedData(Guid userId, CancellationToken ct)
    {
        var ownedItems = await _dataItems.ListActiveOwnedByAsync(userId, ct);
        foreach (var item in ownedItems)
        {
            if (item.MarkDeleted(userId).IsSuccess)
                await _dataItems.UpdateAsync(item, ct);
        }
    }
}
