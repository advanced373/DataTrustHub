using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace DataTrustHub.Features.Tests.WebApp.Helpers;

// One request the WebApp's API clients sent: the method, the path relative to the API base address, the body
internal sealed record RecordedApiRequest(string Method, string Path, string Body);

// One canned API answer: the status code and an optional JSON body
internal sealed record StubResponse(HttpStatusCode StatusCode, string? JsonBody = null);

// Plays the DataTrustHub API for the WebApp: each "METHOD path" answers what the test set up, any other
// request answers 501, and every request is recorded so a test can check which route was called
internal sealed class StubApi
{
    private const string JsonMediaType = "application/json";
    private readonly ConcurrentDictionary<string, StubResponse> responses = new();
    private readonly ConcurrentQueue<RecordedApiRequest> requests = new();

    internal IReadOnlyList<RecordedApiRequest> Requests => [.. requests];

    internal void Reset()
    {
        responses.Clear();
        requests.Clear();
    }

    internal void Answer(HttpMethod method, string path, HttpStatusCode statusCode, string? jsonBody = null) =>
        responses[Key(method.Method, path)] = new StubResponse(statusCode, jsonBody);

    internal bool WasCalled(HttpMethod method, string path) =>
        Requests.Any(request => request.Method == method.Method && request.Path == path);

    internal async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath.TrimStart('/');
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        requests.Enqueue(new RecordedApiRequest(request.Method.Method, path, body));

        if (!responses.TryGetValue(Key(request.Method.Method, path), out var stub))
            return new HttpResponseMessage(HttpStatusCode.NotImplemented);

        var response = new HttpResponseMessage(stub.StatusCode);
        if (stub.JsonBody is not null)
            response.Content = new StringContent(stub.JsonBody, Encoding.UTF8, JsonMediaType);
        return response;
    }

    private static string Key(string method, string path) => $"{method} {path}";
}
