namespace DataTrustHub.Domain.Abstractions;

// Thrown by the unit of work when a save breaks a unique index, e.g. when two concurrent requests both
// passed a uniqueness check such as M1. Use cases catch it to answer with a conflict, without seeing the
// database or EF Core
public sealed class UniqueConstraintViolationException(Exception innerException)
    : Exception(DefaultMessage, innerException)
{
    private const string DefaultMessage = "The save breaks a unique constraint.";
}
