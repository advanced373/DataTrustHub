using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.SharedKernel;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Features.Administration.GetClearance;

public record GetClearanceQuery(Guid ClearanceId) : IRequest<Result<GetClearanceResponse>>;

public class GetClearanceHandler(DContext db) : IRequestHandler<GetClearanceQuery, Result<GetClearanceResponse>>
{
    private readonly DContext _db = db;

    public async Task<Result<GetClearanceResponse>> Handle(
        GetClearanceQuery query,
        CancellationToken cancellationToken)
    {
        var clearance = await _db.Clearances
            .AsNoTracking()
            .Where(c => c.Id == query.ClearanceId)
            .Select(c => new GetClearanceResponse(
                c.Id, c.Name, c.UserId, c.PolicyId, c.ClassificationLevel.Name, c.ClassificationLevel.Priority))
            .FirstOrDefaultAsync(cancellationToken);

        return clearance is null
            ? Result.Failure<GetClearanceResponse>(Errors.ClearanceNotFound)
            : Result.Success(clearance);
    }
}
