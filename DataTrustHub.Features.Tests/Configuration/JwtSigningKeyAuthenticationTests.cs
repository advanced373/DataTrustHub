using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.SharedKernel;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace DataTrustHub.Features.Tests.Configuration;

// AC4: with a valid signing key from configuration the API issues and accepts tokens as before; a token
// signed with any other key is rejected
public class JwtSigningKeyAuthenticationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string AuthorizedEndpoint = "/data";
    private const string RoleClaim = "role";
    private const int TokenLifetimeMinutes = 10;
    private const string OtherSigningKey = "another-signing-key-not-the-api-one-0123456789";

    [Fact]
    public async Task AuthorizedEndpoint_TokenIssuedAtLogin_Returns200()
    {
        var client = await TestUsers.CreateClientAsync(factory, "jwt-key-valid@example.com");

        var response = await client.GetAsync(AuthorizedEndpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AuthorizedEndpoint_TokenSignedWithConfiguredKey_Returns200()
    {
        var token = await CreateTokenForRegisteredUserAsync("jwt-key-same@example.com", TestJwt.SigningKey);

        var response = await GetWithTokenAsync(token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AuthorizedEndpoint_TokenSignedWithDifferentKey_Returns401()
    {
        var token = await CreateTokenForRegisteredUserAsync("jwt-key-other@example.com", OtherSigningKey);

        var response = await GetWithTokenAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Registers a real user, then signs a token for them with the given key; only the key differs
    // between the accepted and the rejected case
    private async Task<string> CreateTokenForRegisteredUserAsync(string email, string signingKey)
    {
        await TestUsers.CreateClientAsync(factory, email);
        var userId = await TestUsers.GetIdAsync(factory, email);

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: TestJwt.Issuer,
            audience: TestJwt.Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, email),
                new Claim(RoleClaim, UserRoles.User)
            ],
            expires: DateTime.UtcNow.AddMinutes(TokenLifetimeMinutes),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private async Task<HttpResponseMessage> GetWithTokenAsync(string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.GetAsync(AuthorizedEndpoint);
    }
}
