using DataTrustHub.Infrastructure.Persistance.Model;
using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.ListOrganizationAuditRecords;

internal static class Constants
{
    internal const string EndpointRoute = "/organizations/{organizationId:guid}/audit-records";
    internal const string EndpointTag = "Administration";

    // The newest records only; no paging in the MVP
    internal const int MaxRecords = 200;

    internal const int ActionMaxLength = DbAuditRecord.ActionMaxLength;
}

internal static class Errors
{
    // Also the answer for a caller who is not an organization admin of the organization, so a caller
    // outside the organization does not learn that it exists
    internal static readonly Error OrganizationNotFound = Error.NotFound(
        "Organization.NotFound",
        "The organization was not found.");
}
