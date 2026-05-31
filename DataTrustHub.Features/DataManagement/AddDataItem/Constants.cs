using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.DataManagement.AddDataItem;

internal static class Constants
{
    internal const string EndpointRoute = "/data/upload";
    internal const string EndpointTag = "DataManagement";
    internal const int MaxFileNameLength = 512;
    internal const long MaxFileSizeBytes = 104_857_600; // 100 MB
}

internal static class Errors
{
    internal static readonly Error NoFileProvided = Error.Failure(
        "DataItem.NoFileProvided",
        "A file must be provided for upload.");

    internal static readonly Error FileTooLarge = Error.Failure(
        "DataItem.FileTooLarge",
        $"File exceeds the maximum allowed size of {Constants.MaxFileSizeBytes} bytes.");

    internal static readonly Error UserNotFound = Error.NotFound(
        "DataItem.UserNotFound",
        "The authenticated user could not be found.");

    internal static readonly Error InvalidUserId = Error.Failure(
        "DataItem.InvalidUserId",
        "The user identity claim is missing or invalid.");
}
