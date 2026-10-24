using DataTrustHub.Infrastructure.Persistance.Model;

namespace DataTrustHub.Features.Administration.ListAuditRecords;

internal static class Constants
{
    internal const string EndpointRoute = "/admin/audit-records";
    internal const string EndpointTag = "Administration";

    // The newest records only; no paging in the MVP
    internal const int MaxRecords = 200;

    internal const int ActionMaxLength = DbAuditRecord.ActionMaxLength;
}

// The tenant-level actions: the platform's own use cases, the only ones the platform admin sees. Actions
// inside an organization (members, policies, clearances, data items) belong to that tenant's trail
// The names mirror the command type names without the Command suffix, as AuditCommandDecorator records them;
// they are not taken with nameof because the commands live in other slices and features
internal static class TenantLevelActions
{
    internal const string CreateOrganization = "CreateOrganization";
    internal const string DeleteOrganization = "DeleteOrganization";
    internal const string AppointOrganizationAdmin = "AppointOrganizationAdmin";
    internal const string CreateAccount = "CreateAccount";
    internal const string DeleteAccount = "DeleteAccount";
    internal const string RegisterUser = "RegisterUser";
    internal const string LoginUser = "LoginUser";

    internal static readonly string[] All =
    [
        CreateOrganization,
        DeleteOrganization,
        AppointOrganizationAdmin,
        CreateAccount,
        DeleteAccount,
        RegisterUser,
        LoginUser
    ];
}
