using DataTrustHub.SharedKernel;

namespace DataTrustHub.Domain.Data
{
    public static class DataErrors
    {
        public static Error NotFound(Guid dataId) => Error.NotFound(
            "Data.NotFound",
            $"The data entry with the Id = '{dataId}' was not found");

        public static readonly Error TitleNotUnique = Error.Conflict(
            "Data.TitleNotUnique",
            "The provided data title is not unique");
    }
}
