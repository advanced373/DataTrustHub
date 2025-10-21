namespace DataTrustHub.Application.Organization.AssignUser
{
    public record AssignUserToOrganizationCommand(Guid UserId, Guid OrganizationId);
}
