using System.Runtime.CompilerServices;

// The equivalence tests hold the SQL form of the read and delete rule (DataAccess) against its entity form
// (#99); no other project sees the infrastructure's internals (ADR-0012 step 5)
[assembly: InternalsVisibleTo("DataTrustHub.Features.Tests")]
