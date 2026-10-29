using DataTrustHub.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace DataTrustHub.Features._Shared.Extensions;

internal static class ResultExtensions
{
    internal static IResult ToHttpResult<T>(this Result<T> result, Func<T, IResult> onSuccess)
    {
        if (result.IsSuccess) return onSuccess(result.Value);

        return result.Error.Type switch
        {
            ErrorType.NotFound => Results.NotFound(result.Error),
            ErrorType.Conflict => Results.Conflict(result.Error),
            ErrorType.Validation => Results.BadRequest(result.Error),
            _ => Results.Problem(result.Error.Description)
        };
    }
}
