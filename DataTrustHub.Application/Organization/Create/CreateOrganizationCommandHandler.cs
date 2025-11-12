using DataTrustHub.Domain.Organization;
using OrganizationValue = DataTrustHub.Domain.Organization.Organization;
using DataTrustHub.SharedKernel;
using MediatR;

namespace DataTrustHub.Application.Organization.Create
{
    public class CreateOrganizationCommandHandler : IRequestHandler<CreateOrganizationCommand, Result<Guid>>
    {
        private readonly IOrganizationRepository _organizationRepository;

        public CreateOrganizationCommandHandler(IOrganizationRepository organizationRepository)
        {
            _organizationRepository = organizationRepository;
        }

        public async Task<Result<Guid>> Handle(CreateOrganizationCommand request, CancellationToken cancellationToken)
        {
            var organizationId = Guid.NewGuid();
            var organization = new OrganizationValue
            {
                Id = organizationId,
                Name = request.Name
            };
            
            await _organizationRepository.AddAsync(organization);
            return Result.Success(organizationId);
        }
    }
}
