using DataTrustHub.SharedKernel;
using MediatR;

namespace DataTrustHub.Application.Organization.Create
{
    public class CreateOrganizationCommandHandler : IRequestHandler<CreateOrganizationCommand, Result<Guid>>
    {
        public async Task<Result<Guid>> Handle(CreateOrganizationCommand request, CancellationToken cancellationToken)
        {
            var organizationId = Guid.NewGuid();
            // TODO: Persiste organizația
            return await Task.FromResult(organizationId);
        }
    }
}
