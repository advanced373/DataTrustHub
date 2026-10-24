namespace DataTrustHub.Features._Shared.Auditing;

// The target type names written to the audit trail; one name per aggregate, shared by every slice
public static class AuditTargetTypes
{
    public const string User = "User";
    public const string Organization = "Organization";
    public const string Policy = "Policy";
    public const string Clearance = "Clearance";
    public const string DataItem = "DataItem";
}
