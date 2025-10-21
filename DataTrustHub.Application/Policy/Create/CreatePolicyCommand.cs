namespace DataTrustHub.Application.Policy.Create
{
    public record CreatePolicyCommand(string Name, Guid OrganizationId);
}
