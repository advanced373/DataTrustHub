using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Features.DataManagement.ListClassificationOptions;

public record ListClassificationOptionsQuery(Guid UserId);

public class ListClassificationOptionsHandler(DContext db)
    : IQueryHandler<ListClassificationOptionsQuery, Result<ClassificationOptionsResponse>>
{
    private readonly DContext _db = db;

    // One entry per policy the user holds a clearance on, with their highest priority there
    public async Task<Result<ClassificationOptionsResponse>> Handle(
        ListClassificationOptionsQuery query,
        CancellationToken cancellationToken)
    {
        var policies = await (
                from clearance in _db.Clearances.AsNoTracking()
                where clearance.UserId == query.UserId
                join policy in _db.Policies on clearance.PolicyId equals policy.Id
                join organization in _db.Organizations on policy.OrganizationId equals organization.Id
                group clearance.ClassificationLevel.Priority
                    by new { policy.Id, policy.Name, GroupName = organization.Name } into byPolicy
                select new ClassificationPolicyOption(
                    byPolicy.Key.Id, byPolicy.Key.Name, byPolicy.Key.GroupName, byPolicy.Max()))
            .ToListAsync(cancellationToken);

        var ordered = policies.OrderBy(p => p.GroupName).ThenBy(p => p.PolicyName).ToList();
        return Result.Success(new ClassificationOptionsResponse(ClassificationLevels.All, ordered));
    }
}
