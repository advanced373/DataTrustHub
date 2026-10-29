namespace DataTrustHub;

internal static class Constants
{
    internal const string ApiSectionName = "Api";
    internal const string AccessTokenClaim = "access_token";
    internal const string JwtRoleClaim = "role";
    internal const string BearerScheme = "Bearer";
    internal const string AdminRole = "Admin";
    internal const string UploadFormField = "file";
    internal const string PolicyFormField = "policyId";
    internal const string LevelFormField = "levelPriority";
    internal const string DefaultContentType = "application/octet-stream";
    internal const long MaxUploadSizeBytes = 104_857_600; // 100 MB, mirrors the API limit
    internal const long MultipartOverheadBytes = 1_048_576; // 1 MB for boundaries and headers
    internal const long MaxUploadRequestBytes = MaxUploadSizeBytes + MultipartOverheadBytes;
    internal const int MinPasswordLength = 8;
    internal const int MaxEmailLength = 256;
    internal const int MaxNameLength = 256;
    internal const string LoginPath = "/Account/Login";
    internal const string LogoutPath = "/Account/Logout";
    internal const string AccessDeniedPath = "/Account/AccessDenied";
}

internal static class ApiRoutes
{
    internal const string Login = "auth/login";
    internal const string Register = "auth/register";
    internal const string UploadData = "data/upload";
    internal const string DataItems = "data";
    internal const string DataItemById = "data/{0}";
    internal const string ClassificationOptions = "data/classification-options";
    internal const string AdminAccounts = "admin/accounts";
    internal const string AdminAccountById = "admin/accounts/{0}";
    internal const string AdminOrganizations = "admin/organizations";
    internal const string AdminOrganizationById = "admin/organizations/{0}";
    internal const string AdminOrganizationAdmins = "admin/organizations/{0}/admins";
    internal const string AdminAuditRecords = "admin/audit-records";

    // Organization-scoped routes; the API answers 404 to a caller without the needed membership there
    internal const string MyOrganizations = "organizations/mine";
    internal const string MyMembership = "organizations/{0}/membership";
    internal const string OrganizationMembers = "organizations/{0}/members";
    internal const string OrganizationMember = "organizations/{0}/members/{1}";
    internal const string OrganizationMemberRole = "organizations/{0}/members/{1}/role";
    internal const string OrganizationFindUserByEmail = "organizations/{0}/users/find-by-email";
    internal const string OrganizationPolicies = "organizations/{0}/policies";
    internal const string OrganizationPolicy = "organizations/{0}/policies/{1}";
    internal const string OrganizationClearances = "organizations/{0}/clearances";
    internal const string OrganizationClearance = "organizations/{0}/clearances/{1}";
    internal const string OrganizationAuditRecords = "organizations/{0}/audit-records";

    // Optional filters of the clearance list and the audit trail; ids, action names and times only,
    // never personal data (ADR-0019)
    internal const string UserIdParameter = "userId";
    internal const string PolicyIdParameter = "policyId";
    internal const string ActionParameter = "action";
    internal const string FromParameter = "from";
    internal const string ToParameter = "to";

    // The round-trip format of a time in the query string, such as 2026-10-24T23:59:59.9999999Z
    internal const string TimeFormat = "O";
}

// Attribute routes of the administration pages, under /Admin/<section>
internal static class AdminRoutes
{
    internal const string Users = "Admin/Users";
    internal const string Organizations = "Admin/Organizations";
    internal const string Audit = "Admin/Audit";
    internal const string Create = "Create";
    internal const string Delete = "{id:guid}/Delete";
    internal const string AppointAdmin = "{id:guid}/AppointAdmin";
}

// Attribute routes of the signed-in user's organization pages, under /Organizations
internal static class OrganizationRoutes
{
    internal const string Root = "Organizations";
    internal const string Leave = "{organizationId:guid}/Leave";
    internal const string Members = "{organizationId:guid}/Members";
    internal const string AddMember = "{organizationId:guid}/Members/Add";
    internal const string ChangeRole = "{organizationId:guid}/Members/{userId:guid}/Role";
    internal const string RemoveMember = "{organizationId:guid}/Members/{userId:guid}/Remove";
    internal const string Policies = "{organizationId:guid}/Policies";
    internal const string CreatePolicy = "{organizationId:guid}/Policies/Create";
    internal const string DeletePolicy = "{organizationId:guid}/Policies/{policyId:guid}/Delete";
    internal const string Clearances = "{organizationId:guid}/Clearances";
    internal const string GrantClearance = "{organizationId:guid}/Clearances/Grant";
    internal const string RevokeClearance = "{organizationId:guid}/Clearances/{clearanceId:guid}/Revoke";
    internal const string Audit = "{organizationId:guid}/Audit";
}

// The standard classification levels offered when granting a clearance; a higher priority sees more
internal static class ClassificationLevels
{
    internal static readonly IReadOnlyList<(string Name, int Priority)> All =
    [
        ("Public", 0),
        ("Internal", 1),
        ("Confidential", 2),
        ("Secret", 3),
        ("Top Secret", 4)
    ];
}

// Membership roles as the API names them (ADR-0014); the UI shows them through LabelOf
internal static class MembershipRoles
{
    internal const string Member = "Member";
    internal const string Guest = "Guest";
    internal const string OrgAdmin = "OrgAdmin";

    // Organization admins are appointed, not added
    internal static readonly IReadOnlyList<string> Addable = [Member, Guest];

    private static readonly Dictionary<string, string> Labels = new()
    {
        [Member] = "Member",
        [Guest] = "Guest",
        [OrgAdmin] = "Organization admin"
    };

    // An unknown role is shown as the API sent it
    internal static string LabelOf(string? role) =>
        role is not null && Labels.TryGetValue(role, out var label) ? label : role ?? string.Empty;
}

// The audited actions offered in the audit trail filters. The names are the command type names without the
// Command suffix, as the API records them; DataItem.Download is the recorded read of a data item's content
internal static class AuditActions
{
    // The actions inside an organization, shown in its audit trail
    internal static readonly IReadOnlyList<string> Organization =
    [
        "AddMember",
        "ChangeMemberRole",
        "RemoveMember",
        "LeaveOrganization",
        "AppointOrganizationAdmin",
        "CreatePolicy",
        "DeletePolicy",
        "CreateClearance",
        "DeleteClearance",
        "DeleteOrganization",
        "AddDataItem",
        "DeleteDataItem",
        "DataItem.Download"
    ];

    // The tenant-level actions, the only ones in the platform admin's audit trail
    internal static readonly IReadOnlyList<string> TenantLevel =
    [
        "CreateOrganization",
        "DeleteOrganization",
        "AppointOrganizationAdmin",
        "CreateAccount",
        "DeleteAccount",
        "RegisterUser",
        "LoginUser"
    ];
}

// How an audited action ended, as the API names the outcome, with its Bootstrap badge class
internal static class AuditOutcomes
{
    internal const string Succeeded = "Succeeded";
    internal const string Rejected = "Rejected";
    internal const string Denied = "Denied";
    internal const string Failed = "Failed";

    private const string UnknownBadgeClass = "text-bg-secondary";

    private static readonly Dictionary<string, string> BadgeClasses = new()
    {
        [Succeeded] = "text-bg-success",
        [Rejected] = "text-bg-warning",
        [Denied] = "text-bg-danger",
        [Failed] = "text-bg-dark"
    };

    // An unknown outcome gets a neutral badge
    internal static string BadgeClassOf(string outcome) =>
        BadgeClasses.GetValueOrDefault(outcome, UnknownBadgeClass);
}

// Display rules of the audit trail table
internal static class AuditTrailDisplay
{
    // The API returns at most this many records, the newest first
    internal const int MaxRecords = 200;

    // Times are shown in UTC, as the API returns them
    internal const string TimeFormat = "yyyy-MM-dd HH:mm:ss 'UTC'";

    // A target id is shown by its first characters; the full id is in the tooltip
    internal const int ShortIdLength = 8;

    // The value format of a date input
    internal const string DateInputFormat = "yyyy-MM-dd";
}

internal static class ControllerNames
{
    internal const string Account = "Account";
    internal const string Home = "Home";
    internal const string Data = "Data";
    internal const string Admin = "Admin";
    internal const string Users = "Users";
    internal const string Organizations = "Organizations";
    internal const string MyOrganizations = "MyOrganizations";
    internal const string OrganizationPolicies = "OrganizationPolicies";
    internal const string OrganizationClearances = "OrganizationClearances";
    internal const string OrganizationAudit = "OrganizationAudit";
    internal const string Audit = "Audit";
}

internal static class TempDataKeys
{
    internal const string Success = "Success";
    internal const string Warning = "Warning";
}

internal static class Messages
{
    internal const string InvalidCredentials = "Invalid email or password.";
    internal const string ApiUnavailable = "The DataTrustHub API is unavailable. Try again later.";
    internal const string RequestFailed = "The request could not be completed.";
    internal const string UnexpectedResponse = "The API returned an unexpected response.";
    internal const string Forbidden = "You do not have permission to perform this action.";
    internal const string SessionExpired = "Your session has expired. Please sign in again.";
    internal const string Registered = "Account created. You can now sign in.";
    internal const string FileUploaded = "File \"{0}\" uploaded.";
    internal const string FileDeleted = "File deleted.";
    internal const string DownloadFailed = "The file content could not be read.";
    internal const string LevelRequiredForPolicy = "Select a classification level for the policy.";
    internal const string LevelAboveClearance = "You can classify at most at your own clearance level on this policy.";
    internal const string AccountCreated = "Account {0} created.";
    internal const string AccountDeleted = "Account deleted. Its clearances were removed and its data was deleted.";
    internal const string OrganizationCreated = "Organization \"{0}\" created.";
    internal const string OrganizationDeleted = "Organization deleted.";
    internal const string OrganizationNotFound = "The organization was not found.";
    internal const string OrganizationAdminAppointed = "{0} was appointed organization admin.";
    internal const string OrganizationLeft = "You left the organization. Your clearances there were revoked.";
    internal const string NoUserWithEmail = "No user has this email address.";
    internal const string AlreadyMember = "{0} is already in this organization, as {1}.";
    internal const string MemberAdded = "{0} added as {1}.";
    internal const string MemberRoleChanged = "Role changed to {0}.";
    internal const string MemberRemoved = "Member removed. Their clearances in this organization were revoked.";
    internal const string PolicyCreated = "Policy \"{0}\" created.";
    internal const string PolicyDeleted = "Policy deleted.";
    internal const string ClearanceGranted = "Clearance granted to {0}.";
    internal const string ClearanceRevoked = "Clearance revoked.";
    internal const string DefaultClearanceName = "{0} on {1}";
    internal const string MemberRequired = "Select a member.";
    internal const string PolicyRequired = "Select a policy.";
    internal const string LevelRequired = "Select a classification level.";
    internal const string UnknownMember = "(former member)";
    internal const string UnknownPolicy = "(deleted policy)";
    internal const string AnonymousActor = "Anonymous";
    internal const string DeletedAccount = "Deleted account";
    internal const string AuditTrailTruncated = "Showing the {0} most recent records; narrow the filters.";
    internal const string FileRequired = "Select a file to upload.";
    internal const string FileTooLarge = "The file exceeds the maximum size of 100 MB.";
}
