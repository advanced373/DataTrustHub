using DataTrustHub.SharedKernel;

namespace DataTrustHub.Domain.Clearance
{
    public static class ClearanceErrors
    {
        public static Error NotFound(Guid clearanceId) => Error.NotFound(
            "Clearance.NotFound",
            $"The clearance with the Id = '{clearanceId}' was not found");

        public static readonly Error NameNotUnique = Error.Conflict(
            "Clearance.NameNotUnique",
            "The provided clearance name is not unique");
    }
}
