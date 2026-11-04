
namespace DataTrustHub.SharedKernel
{
    public enum ErrorType
    {
        Failure = 0,
        Validation = 1,
        Problem = 2,
        NotFound = 3,
        Conflict = 4,
        // The service cannot take the request now, e.g. a queue is full; the client may try again later (503)
        Unavailable = 5
    }
}
