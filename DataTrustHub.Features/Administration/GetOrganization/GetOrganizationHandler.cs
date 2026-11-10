using DataTrustHub.Domain.ReadModels.Organizations;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.GetOrganization;

public record GetOrganizationQuery(Guid OrganizationId);

public class GetOrganizationHandler(IOrganizationQueries organizationQueries)
    : IQueryHandler<GetOrganizationQuery, Result<GetOrganizationResponse>>
{
    private readonly IOrganizationQueries _organizationQueries = organizationQueries;

    public async Task<Result<GetOrganizationResponse>> Handle(
        GetOrganizationQuery query,
        CancellationToken cancellationToken)
    {
        var organization = await _organizationQueries.FindAsync(query.OrganizationId, cancellationToken);

        return organization is null
            ? Result.Failure<GetOrganizationResponse>(Errors.OrganizationNotFound)
            : Result.Success(new GetOrganizationResponse(organization.Id, organization.Name));
    }
}
