using DataTrustHub.Application.Abstractions.Messaging;
using DataTrustHub.Domain.Clearance;
using DataTrustHub.SharedKernel;
using ClearanceValue = DataTrustHub.Domain.Clearance.Clearance;

namespace DataTrustHub.Application.Clearance.Get;

public sealed class GetClearanceByIdQueryHandler : IQueryHandler<GetClearanceByIdQuery, ClearanceValue>
{
    private readonly IClearanceRepository _clearanceRepository;

    public GetClearanceByIdQueryHandler(IClearanceRepository clearanceRepository)
    {
        _clearanceRepository = clearanceRepository;
    }

    public async Task<Result<ClearanceValue>> Handle(GetClearanceByIdQuery request, CancellationToken cancellationToken)
    {
        var clearance = await _clearanceRepository.GetByIdAsync(request.ClearanceId);

        return clearance is null
            ? Result.Failure<ClearanceValue>(Error.NotFound("Clearance.NotFound", $"Clearance with ID {request.ClearanceId} was not found."))
            : Result.Success(clearance);
    }
}
