using MediatR;

namespace DataTrustHub.Application.Organization.AssignUser
{
    public class AssignUserToOrganizationCommandHandler : IRequestHandler<AssignUserToOrganizationCommand, bool>
    {
        public async Task<bool> Handle(AssignUserToOrganizationCommand request, CancellationToken cancellationToken)
        {
            // TODO: Persiste user-organization assignment
            return await Task.FromResult(true);
        }
    }
}
