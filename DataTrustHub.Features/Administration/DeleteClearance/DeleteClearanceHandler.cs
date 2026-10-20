using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.SharedKernel;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Features.Administration.DeleteClearance;

public record DeleteClearanceCommand(Guid ClearanceId) : IRequest<Result<Guid>>;

public class DeleteClearanceHandler(DContext db) : IRequestHandler<DeleteClearanceCommand, Result<Guid>>
{
    private readonly DContext _db = db;

    // The classification level stays: other clearances may share it
    public async Task<Result<Guid>> Handle(
        DeleteClearanceCommand command,
        CancellationToken cancellationToken)
    {
        var clearance = await _db.Clearances.FirstOrDefaultAsync(c => c.Id == command.ClearanceId, cancellationToken);
        if (clearance is null) return Result.Failure<Guid>(Errors.ClearanceNotFound);

        _db.Clearances.Remove(clearance);
        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success(command.ClearanceId);
    }
}
