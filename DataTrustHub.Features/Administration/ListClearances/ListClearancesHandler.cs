using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.SharedKernel;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Features.Administration.ListClearances;

public record ListClearancesQuery(Guid? UserId, Guid? PolicyId)
    : IRequest<Result<IReadOnlyList<ClearanceSummary>>>;

public class ListClearancesHandler(DContext db)
    : IRequestHandler<ListClearancesQuery, Result<IReadOnlyList<ClearanceSummary>>>
{
    private readonly DContext _db = db;

    public async Task<Result<IReadOnlyList<ClearanceSummary>>> Handle(
        ListClearancesQuery query,
        CancellationToken cancellationToken)
    {
        var clearances = await _db.Clearances
            .AsNoTracking()
            .Where(c => query.UserId == null || c.UserId == query.UserId)
            .Where(c => query.PolicyId == null || c.PolicyId == query.PolicyId)
            .OrderBy(c => c.Name)
            .Select(c => new ClearanceSummary(
                c.Id, c.Name, c.UserId, c.PolicyId, c.ClassificationLevel.Name, c.ClassificationLevel.Priority))
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<ClearanceSummary>>(clearances);
    }
}
