using DataTrustHub.Application.Abstractions.Messaging;
using DataTrustHub.Domain.Organization;
using DataTrustHub.SharedKernel;
using MediatR;

namespace DataTrustHub.Application.Organization.Delete
{
    public class DeleteOrganizationCommandHandler : IRequestHandler<DeleteOrganizationCommand, Result>
    {
        private readonly IOrganizationRepository _organizationRepository;

        public DeleteOrganizationCommandHandler(IOrganizationRepository organizationRepository)
        {
            _organizationRepository = organizationRepository;
        }

        public async Task<Result> Handle(DeleteOrganizationCommand request, CancellationToken cancellationToken)
        {
            var organization = await _organizationRepository.GetByIdAsync(request.OrganizationId);

            if (organization is null)
            {
                return Result.Failure(Error.NotFound(
                    "Organization.NotFound",
                    $"The organization with the Id = '{request.OrganizationId}' was not found"));
            }

            await _organizationRepository.DeleteAsync(request.OrganizationId);
            return Result.Success();
        }
    }
}

