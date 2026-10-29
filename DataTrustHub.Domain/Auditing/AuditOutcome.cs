namespace DataTrustHub.Domain.Auditing;

// How an audited action ended (ADR-0016); stored by name
public enum AuditOutcome
{
    // The action did what was asked
    Succeeded,

    // Refused by input validation or a domain error; the record holds the error code
    Rejected,

    // A read the user may not make, answered as "not found" (need-to-know)
    Denied,

    // An exception; the record holds the exception type name
    Failed
}
