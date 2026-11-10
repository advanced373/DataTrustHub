using DataTrustHub.Domain.ReadModels.Memberships;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.ListMyOrganizations;

public record ListMyOrganizationsQuery(Guid CallerId);

// A user lists the organizations they belong to, with their membership role in each (ADR-0014)
public class ListMyOrganizationsHandler(IMembershipQueries membershipQueries)
    : IQueryHandler<ListMyOrganizationsQuery, Result<IReadOnlyList<MyOrganizationSummary>>>
{
    private readonly IMembershipQueries _membershipQueries = membershipQueries;

    public async Task<Result<IReadOnlyList<MyOrganizationSummary>>> Handle(
        ListMyOrganizationsQuery query,
        CancellationToken cancellationToken)
    {
        var organizations = await _membershipQueries.ListMyOrganizationsAsync(query.CallerId, cancellationToken);
        return Result.Success<IReadOnlyList<MyOrganizationSummary>>(organizations.Select(ToSummary).ToList());
    }

    private static MyOrganizationSummary ToSummary(MyOrganization organization) =>
        new(organization.OrganizationId, organization.OrganizationName, organization.MembershipId, organization.Role);
}
