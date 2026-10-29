using System.Runtime.CompilerServices;

// The persistence mappers rebuild entities through their internal constructors (ADR-0012);
// no other project sees the domain's internals
[assembly: InternalsVisibleTo("DataTrustHub.Infrastructure")]
