using System.Net;
using DataTrustHub.Features.Tests.Health.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using Xunit;

namespace DataTrustHub.Features.Tests.Health;

// #146 AC1 and AC3 on the API: GET /health answers 200 anonymously without the database, and the other
// routes, their authorization and the OpenAPI document behave as before
public class ApiHealthTests(NoDatabaseApiFactory factory) : IClassFixture<NoDatabaseApiFactory>
{
    private const string ProtectedRoute = "/data";
    private const string LoginRoute = "/auth/login";
    private const string OpenApiDocumentRoute = "/openapi/v1.json";

    [Fact]
    public async Task Health_Anonymous_Returns200()
    {
        var response = await factory.CreateClient().GetAsync(HealthProbe.Route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Health_Anonymous_BodyMeansHealthy()
    {
        var response = await factory.CreateClient().GetAsync(HealthProbe.Route);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(HealthProbe.HealthyBody, body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Health_UnreachableDatabase_DoesNotResolveDContext()
    {
        await factory.CreateClient().GetAsync(HealthProbe.Route);

        Assert.Equal(0, factory.DatabaseTouches.Count);
    }

    [Fact]
    public async Task Health_WithInvalidToken_Returns200()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "not-a-jwt");

        var response = await client.GetAsync(HealthProbe.Route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedRoute_Anonymous_StillReturns401()
    {
        var response = await factory.CreateClient().GetAsync(ProtectedRoute);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task OpenApiDocument_StillListsFeatureRoutes()
    {
        var response = await factory.CreateClient().GetAsync(OpenApiDocumentRoute);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await response.Content.ReadAsStringAsync();
        Assert.Contains(LoginRoute, document);
        Assert.Contains($"\"{ProtectedRoute}\"", document);
    }
}

// A plain test host (EF InMemory) also answers the health endpoint, so it does not depend on the fixture
public class ApiHealthOnInMemoryDatabaseTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Health_DatabaseAvailable_Returns200()
    {
        var response = await factory.CreateClient().GetAsync(HealthProbe.Route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
