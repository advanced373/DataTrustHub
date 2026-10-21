using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Features.Administration.GetGroup;

public record GetGroupQuery(Guid GroupId);

public class GetGroupHandler(DContext db) : IQueryHandler<GetGroupQuery, Result<GetGroupResponse>>
{
    private readonly DContext _db = db;

    public async Task<Result<GetGroupResponse>> Handle(
        GetGroupQuery query,
        CancellationToken cancellationToken)
    {
        var group = await _db.Organizations
            .AsNoTracking()
            .Where(o => o.Id == query.GroupId)
            .Select(o => new GetGroupResponse(o.Id, o.Name))
            .FirstOrDefaultAsync(cancellationToken);

        return group is null
            ? Result.Failure<GetGroupResponse>(Errors.GroupNotFound)
            : Result.Success(group);
    }
}
