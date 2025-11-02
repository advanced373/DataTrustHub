using DataTrustHub.SharedKernel;

namespace DataTrustHub.Domain.Policy
{
    public static class PolicyErrors
    {
        public static Error NotFound(Guid policyId) => Error.NotFound(
            "Policy.NotFound",
            $"The policy with the Id = '{policyId}' was not found");

        public static readonly Error NameNotUnique = Error.Conflict(
            "Policy.NameNotUnique",
            "The provided policy name is not unique");
    }
}
