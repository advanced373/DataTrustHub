using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.SharedKernel;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Features.Administration.GetPolicy;

public record GetPolicyQuery(Guid PolicyId) : IRequest<Result<GetPolicyResponse>>;

public class GetPolicyHandler(DContext db) : IRequestHandler<GetPolicyQuery, Result<GetPolicyResponse>>
{
    private readonly DContext _db = db;

    public async Task<Result<GetPolicyResponse>> Handle(
        GetPolicyQuery query,
        CancellationToken cancellationToken)
    {
        var policy = await _db.Policies
            .AsNoTracking()
            .Where(p => p.Id == query.PolicyId)
            .Select(p => new GetPolicyResponse(p.Id, p.Name, p.OrganizationId))
            .FirstOrDefaultAsync(cancellationToken);

        return policy is null
            ? Result.Failure<GetPolicyResponse>(Errors.PolicyNotFound)
            : Result.Success(policy);
    }
}
