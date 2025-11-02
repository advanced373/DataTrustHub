using DataTrustHub.SharedKernel;
using MediatR;

namespace DataTrustHub.Application.Organization.AssignUser
{
    public class AssignUserToOrganizationCommandHandler : IRequestHandler<AssignUserToOrganizationCommand, Result<bool>>
    {
        public async Task<Result<bool>> Handle(AssignUserToOrganizationCommand request, CancellationToken cancellationToken)
        {
            // TODO: Persiste user-organization assignment
            return await Task.FromResult(Result.Success(true));
        }
    }
}
