namespace DataTrustHub.Features.Administration.ListMyOrganizations;

internal static class Constants
{
    // "mine" is not a guid, so it does not clash with the /organizations/{organizationId:guid}/... routes
    internal const string EndpointRoute = "/organizations/mine";
    internal const string EndpointTag = "Administration";
}
