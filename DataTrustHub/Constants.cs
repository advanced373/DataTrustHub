namespace DataTrustHub;

internal static class Constants
{
    internal const string ApiSectionName = "Api";
    internal const string AccessTokenClaim = "access_token";
    internal const string JwtRoleClaim = "role";
    internal const string BearerScheme = "Bearer";
    internal const string AdminRole = "Admin";
    internal const string UploadFormField = "file";
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
    internal const string AdminAccounts = "admin/accounts";
    internal const string AdminPolicies = "admin/policies";
    internal const string AdminGroups = "admin/groups";
    internal const string AdminGroupById = "admin/groups/{0}";
}

// Attribute routes of the administration pages, under /Admin/<section>
internal static class AdminRoutes
{
    internal const string Groups = "Admin/Groups";
    internal const string Create = "Create";
    internal const string Delete = "{id:guid}/Delete";
}

internal static class ControllerNames
{
    internal const string Account = "Account";
    internal const string Home = "Home";
    internal const string Data = "Data";
    internal const string Admin = "Admin";
    internal const string Groups = "Groups";
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
    internal const string FileUploaded = "File uploaded. Item id: {0}";
    internal const string AccountCreated = "Account created. User id: {0}";
    internal const string PolicyCreated = "Policy created. Policy id: {0}";
    internal const string GroupCreated = "Group \"{0}\" created.";
    internal const string GroupDeleted = "Group deleted.";
    internal const string FileRequired = "Select a file to upload.";
    internal const string FileTooLarge = "The file exceeds the maximum size of 100 MB.";
}
