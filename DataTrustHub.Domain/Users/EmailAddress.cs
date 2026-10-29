namespace DataTrustHub.Domain.Users;

// An email address in its normalized form: surrounding whitespace trimmed and the whole address lower-cased
// with the invariant culture, so " Ion@Firma.RO " and "ion@firma.ro" are the same address (U7).
// This is the only place that implements the rule; the normalized form is stored, compared and returned
public sealed record EmailAddress
{
    private EmailAddress(string value) => Value = value;

    public string Value { get; }

    public static EmailAddress From(string email) => new(email.Trim().ToLowerInvariant());

    public override string ToString() => Value;
}
