using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.SharedKernel;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Features.Administration.DeleteAccount;

public record DeleteAccountCommand(Guid AccountId, Guid RequesterId) : IRequest<Result<Guid>>;

public class DeleteAccountHandler(DContext db) : IRequestHandler<DeleteAccountCommand, Result<Guid>>
{
    private readonly DContext _db = db;

    // Removes the user and their clearances and soft-deletes the data they own, in one save
    public async Task<Result<Guid>> Handle(
        DeleteAccountCommand command,
        CancellationToken cancellationToken)
    {
        // Guards against an admin locking themselves out
        if (command.AccountId == command.RequesterId)
            return Result.Failure<Guid>(Errors.CannotDeleteOwnAccount);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == command.AccountId, cancellationToken);
        if (user is null) return Result.Failure<Guid>(Errors.AccountNotFound);

        await RemoveClearances(command.AccountId, cancellationToken);
        await SoftDeleteOwnedData(command.AccountId, cancellationToken);
        _db.Users.Remove(user);
        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success(command.AccountId);
    }

    private async Task RemoveClearances(Guid userId, CancellationToken ct)
    {
        var clearances = await _db.Clearances.Where(c => c.UserId == userId).ToListAsync(ct);
        _db.Clearances.RemoveRange(clearances);
    }

    private async Task SoftDeleteOwnedData(Guid userId, CancellationToken ct)
    {
        var items = await _db.DataItems.Where(d => d.OwnerUserId == userId && !d.IsDeleted).ToListAsync(ct);
        items.ForEach(item => item.IsDeleted = true);
    }
}
