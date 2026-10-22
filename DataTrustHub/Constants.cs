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
    internal const string AdminPolicies = "admin/policies";
    internal const string AdminOrganizations = "admin/organizations";
    internal const string AdminOrganizationById = "admin/organizations/{0}";
    internal const string AdminPolicyById = "admin/policies/{0}";
    internal const string AdminPoliciesByOrganization = "admin/policies?organizationId={0}";
    internal const string AdminClearances = "admin/clearances";
    internal const string AdminClearanceById = "admin/clearances/{0}";
    internal const string AdminClearancesByUser = "admin/clearances?userId={0}";
}

// Attribute routes of the administration pages, under /Admin/<section>
internal static class AdminRoutes
{
    internal const string Users = "Admin/Users";
    internal const string Groups = "Admin/Groups";
    internal const string Policies = "Admin/Policies";
    internal const string Clearances = "Admin/Clearances";
    internal const string Create = "Create";
    internal const string Delete = "{id:guid}/Delete";
}

// Classification levels offered when granting a clearance; a higher priority sees more
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

internal static class ControllerNames
{
    internal const string Account = "Account";
    internal const string Home = "Home";
    internal const string Data = "Data";
    internal const string Admin = "Admin";
    internal const string Users = "Users";
    internal const string Groups = "Groups";
    internal const string Policies = "Policies";
    internal const string Clearances = "Clearances";
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
    internal const string PolicyCreated = "Policy \"{0}\" created.";
    internal const string PolicyDeleted = "Policy deleted.";
    internal const string GroupCreated = "Group \"{0}\" created.";
    internal const string GroupDeleted = "Group deleted.";
    internal const string FileRequired = "Select a file to upload.";
    internal const string GroupRequired = "Select a group.";
    internal const string UnknownGroup = "(deleted group)";
    internal const string UnknownUser = "(deleted user)";
    internal const string UnknownPolicy = "(deleted policy)";
    internal const string UserRequired = "Select a user.";
    internal const string PolicyRequired = "Select a policy.";
    internal const string LevelRequired = "Select a classification level.";
    internal const string ClearanceGranted = "Clearance granted to {0}.";
    internal const string ClearanceRevoked = "Clearance revoked.";
    internal const string DefaultClearanceName = "{0} on {1}";
    internal const string FileTooLarge = "The file exceeds the maximum size of 100 MB.";
}
