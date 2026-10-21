using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Clearances;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.DeleteClearance;

public record DeleteClearanceCommand(Guid ClearanceId);

public class DeleteClearanceHandler(
    IClearanceRepository clearances,
    IUnitOfWork unitOfWork)
    : ICommandHandler<DeleteClearanceCommand, Result<Guid>>
{
    private readonly IClearanceRepository _clearances = clearances;
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    // The classification level stays: other clearances may share it
    public async Task<Result<Guid>> Handle(
        DeleteClearanceCommand command,
        CancellationToken cancellationToken)
    {
        var clearance = await _clearances.GetByIdAsync(command.ClearanceId, cancellationToken);
        if (clearance is null) return Result.Failure<Guid>(Errors.ClearanceNotFound);

        await _clearances.RemoveAsync(clearance, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(command.ClearanceId);
    }
}
