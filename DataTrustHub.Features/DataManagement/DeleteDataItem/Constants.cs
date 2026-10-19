using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.DataManagement.DeleteDataItem;

internal static class Constants
{
    internal const string EndpointRoute = "/data/{id:guid}";
    internal const string EndpointTag = "DataManagement";
}

internal static class Errors
{
    internal static readonly Error DataItemNotFound = Error.NotFound(
        "DataItem.NotFound",
        "The data item was not found.");
}
