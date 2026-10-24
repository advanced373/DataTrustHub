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

    // Organization-scoped routes; the API answers 404 to a caller without the needed membership there
    internal const string MyOrganizations = "organizations/mine";
    internal const string MyMembership = "organizations/{0}/membership";
    internal const string OrganizationMembers = "organizations/{0}/members";
    internal const string OrganizationMember = "organizations/{0}/members/{1}";
    internal const string OrganizationMemberRole = "organizations/{0}/members/{1}/role";
    internal const string OrganizationFindUserByEmail = "organizations/{0}/users/find-by-email";
}

// Attribute routes of the administration pages, under /Admin/<section>
internal static class AdminRoutes
{
    internal const string Users = "Admin/Users";
    internal const string Organizations = "Admin/Organizations";
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

internal static class ControllerNames
{
    internal const string Account = "Account";
    internal const string Home = "Home";
    internal const string Data = "Data";
    internal const string Admin = "Admin";
    internal const string Users = "Users";
    internal const string Organizations = "Organizations";
    internal const string MyOrganizations = "MyOrganizations";
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
    internal const string FileRequired = "Select a file to upload.";
    internal const string FileTooLarge = "The file exceeds the maximum size of 100 MB.";
}
