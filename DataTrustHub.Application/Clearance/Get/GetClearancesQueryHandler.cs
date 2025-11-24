using DataTrustHub.Application.Abstractions.Messaging;
using DataTrustHub.Domain.Clearance;
using DataTrustHub.SharedKernel;
using ClearanceValue = DataTrustHub.Domain.Clearance.Clearance;

namespace DataTrustHub.Application.Clearance.Get;

public sealed class GetClearancesQueryHandler : IQueryHandler<GetClearancesQuery, List<ClearanceValue>>
{
    private readonly IClearanceRepository _clearanceRepository;

    public GetClearancesQueryHandler(IClearanceRepository clearanceRepository)
    {
        _clearanceRepository = clearanceRepository;
    }

    public async Task<Result<List<ClearanceValue>>> Handle(GetClearancesQuery request, CancellationToken cancellationToken)
    {
        var clearances = await _clearanceRepository.GetAllAsync();
        return Result.Success(clearances);
    }
}
