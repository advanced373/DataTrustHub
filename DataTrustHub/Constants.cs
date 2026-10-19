namespace DataTrustHub;

internal static class Constants
{
    internal const string ApiSectionName = "Api";
    internal const string AccessTokenClaim = "access_token";
    internal const string BearerScheme = "Bearer";
    internal const string UploadFormField = "file";
}

internal static class ApiRoutes
{
    internal const string Login = "auth/login";
    internal const string Register = "auth/register";
    internal const string UploadData = "data/upload";
    internal const string AdminAccounts = "admin/accounts";
    internal const string AdminPolicies = "admin/policies";
    internal const string AdminGroups = "admin/groups";
}

internal static class Messages
{
    internal const string InvalidCredentials = "Invalid email or password.";
    internal const string ApiUnavailable = "The DataTrustHub API is unavailable. Try again later.";
    internal const string RequestFailed = "The request could not be completed.";
    internal const string UnexpectedResponse = "The API returned an unexpected response.";
    internal const string Forbidden = "You do not have permission to perform this action.";
    internal const string SessionExpired = "Your session has expired. Please sign in again.";
}
