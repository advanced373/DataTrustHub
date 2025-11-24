using DataTrustHub.Application.Abstractions.Messaging;
using DataTrustHub.Domain.Organization;
using DataTrustHub.SharedKernel;
using OrganizationValue = DataTrustHub.Domain.Organization.Organization;

namespace DataTrustHub.Application.Organization.Get;

public sealed class GetOrganizationsQueryHandler : IQueryHandler<GetOrganizationsQuery, List<OrganizationValue>>
{
    private readonly IOrganizationRepository _organizationRepository;

    public GetOrganizationsQueryHandler(IOrganizationRepository organizationRepository)
    {
        _organizationRepository = organizationRepository;
    }

    public async Task<Result<List<OrganizationValue>>> Handle(GetOrganizationsQuery request, CancellationToken cancellationToken)
    {
        var organizations = await _organizationRepository.GetAllAsync();
        return Result.Success(organizations);
    }
}
