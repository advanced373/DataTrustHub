using DataTrustHub.SharedKernel;

namespace DataTrustHub.Domain.DataItems;

public static class DataItemErrors
{
    public static readonly Error NotFound = Error.NotFound(
        "DataItem.NotFound",
        "The data item was not found.");

    // A validation error, so the client gets 400 with the reason
    public static readonly Error NotCleared = new(
        "DataItem.NotCleared",
        "You can classify data only under a policy you hold a clearance on, and at most at your clearance level.",
        ErrorType.Validation);
}
