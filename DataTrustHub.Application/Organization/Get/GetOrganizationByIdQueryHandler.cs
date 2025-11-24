using DataTrustHub.Application.Abstractions.Messaging;
using DataTrustHub.Domain.Organization;
using DataTrustHub.SharedKernel;
using OrganizationValue = DataTrustHub.Domain.Organization.Organization;

namespace DataTrustHub.Application.Organization.Get;

public sealed class GetOrganizationByIdQueryHandler : IQueryHandler<GetOrganizationByIdQuery, OrganizationValue>
{
    private readonly IOrganizationRepository _organizationRepository;

    public GetOrganizationByIdQueryHandler(IOrganizationRepository organizationRepository)
    {
        _organizationRepository = organizationRepository;
    }

    public async Task<Result<OrganizationValue>> Handle(GetOrganizationByIdQuery request, CancellationToken cancellationToken)
    {
        var organization = await _organizationRepository.GetByIdAsync(request.OrganizationId);

        return organization is null
            ? Result.Failure<OrganizationValue>(Error.NotFound("Organization.NotFound", $"Organization with ID {request.OrganizationId} was not found."))
            : Result.Success(organization);
    }
}
