namespace DataTrustHub.Features._Shared.Auditing;

internal static class AuditConstants
{
    // Stripped from a command type name to get the audited action: DeleteDataItemCommand -> DeleteDataItem
    internal const string CommandSuffix = "Command";

    // Stripped from an audited query type name the same way: FindUserByEmailQuery -> FindUserByEmail
    internal const string QuerySuffix = "Query";

    // Names the action and the outcome only, never the record's other data
    internal const string AuditWriteFailedMessage =
        "The audit record of {Action} with outcome {Outcome} could not be written ({ExceptionType}).";
}
