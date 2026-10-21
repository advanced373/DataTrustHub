using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace DataTrustHub.Features.Tests.Administration.Helpers;

// Asserts the status and the serialized Error (code and message) of a failed request
internal static class ErrorAssert
{
    internal static async Task HasErrorAsync(
        HttpResponseMessage response, HttpStatusCode expectedStatus, string expectedCode, string expectedMessage)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal((expectedCode, expectedMessage), (error!.Code, error.Description));
    }

    private record ErrorResponse(string Code, string Description);
}
