using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.DataManagement.GetDataItem;

internal static class Constants
{
    internal const string EndpointRoute = "/data/{id:guid}";
    internal const string EndpointTag = "DataManagement";

    // The audited access name of a download and of a denied read (ADR-0016 point 2)
    internal const string DownloadAction = "DataItem.Download";

    // Names the action, the outcome, the target and the actor only, never the record's other data (A2)
    internal const string DeniedAuditWriteFailedMessage =
        "The audit record of {Action} with outcome {Outcome} on data item {TargetId} by {ActorUserId} " +
        "could not be written ({ExceptionType}).";
}

internal static class Errors
{
    internal static readonly Error DataItemNotFound = Error.NotFound(
        "DataItem.NotFound",
        "The data item was not found.");
}
