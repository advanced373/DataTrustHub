using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.SharedKernel;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Features.Administration.ListGroups;

public record ListGroupsQuery : IRequest<Result<IReadOnlyList<GroupSummary>>>;

public class ListGroupsHandler(DContext db)
    : IRequestHandler<ListGroupsQuery, Result<IReadOnlyList<GroupSummary>>>
{
    private readonly DContext _db = db;

    public async Task<Result<IReadOnlyList<GroupSummary>>> Handle(
        ListGroupsQuery query,
        CancellationToken cancellationToken)
    {
        var groups = await _db.Organizations
            .AsNoTracking()
            .OrderBy(o => o.Name)
            .Select(o => new GroupSummary(o.Id, o.Name))
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<GroupSummary>>(groups);
    }
}
