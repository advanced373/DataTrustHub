using DataTrustHub.Domain.ReadModels.Organizations;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.ListOrganizations;

public record ListOrganizationsQuery;

public class ListOrganizationsHandler(IOrganizationQueries organizationQueries)
    : IQueryHandler<ListOrganizationsQuery, Result<IReadOnlyList<OrganizationSummary>>>
{
    private readonly IOrganizationQueries _organizationQueries = organizationQueries;

    public async Task<Result<IReadOnlyList<OrganizationSummary>>> Handle(
        ListOrganizationsQuery query,
        CancellationToken cancellationToken)
    {
        var organizations = await _organizationQueries.ListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<OrganizationSummary>>(organizations.Select(ToSummary).ToList());
    }

    private static OrganizationSummary ToSummary(OrganizationEntry organization) =>
        new(organization.Id, organization.Name);
}
