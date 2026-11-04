using System.Net;
using System.Net.Http.Json;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Authentication.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataTrustHub.Features.Tests.Authentication;

// AC2 and AC7 of #123: registration stores the normalized email (U7), a case or space variant of an existing
// address is the same account (U1), and the format and length rules (U2) apply to the trimmed address. Since #172
// register stores a registration request (202) and the account exists only once it is approved
public class RegisterUserEmailNormalizationTests(RegistrationApiFactory factory) : IClassFixture<RegistrationApiFactory>
{
    private const string RegisterEndpoint = "/auth/register";
    private const string Password = "Password1!";
    private const string EmailDomain = "@example.com";
    private const int MaxEmailLength = 256;
    private const string SurroundingSpaces = "   ";

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task RegisterUser_EmailWithCaseAndSurroundingSpaces_StoresTheNormalizedEmail()
    {
        var response = await RegisterAsync(" Reg.Norm.Stored@Example.COM ");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var requestId = (await response.Content.ReadFromJsonAsync<RegisterUserResponse>())!.RegistrationRequestId;
        Assert.Equal("reg.norm.stored@example.com", await StoredEmailAsync(requestId));
    }

    // The variants themselves are pinned on EmailAddress (EmailAddressTests); here one variant each way:
    // the request is a variant, and the existing account was created with one
    [Theory]
    [InlineData("reg_dup_variant@example.com", "\tReg_Dup_Variant@Example.Com ")]
    [InlineData(" Reg_Dup_Reverse@Example.COM ", "reg_dup_reverse@example.com")]
    public async Task RegisterUser_VariantOfAnExistingEmail_Returns409EmailAlreadyInUse(string existing, string variant)
    {
        await TestUsers.CreateAsync(factory, existing, Password);

        var response = await RegisterAsync(variant);

        await ErrorAssert.HasErrorAsync(
            response, HttpStatusCode.Conflict, AuthErrorCodes.EmailAlreadyInUse, AuthErrorMessages.EmailAlreadyInUse);
    }

    // --- AC7: validation on the trimmed address ---

    [Theory]
    [InlineData("")]
    [InlineData(" \t ")]
    public async Task RegisterUser_EmptyOrWhitespaceOnlyEmail_Returns400(string email)
    {
        var response = await RegisterAsync(email);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RegisterUser_MalformedEmailWithSurroundingSpaces_Returns400()
    {
        var response = await RegisterAsync(" not-an-email ");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RegisterUser_TrimmedEmailOfMaxLengthWithSurroundingSpaces_Returns202()
    {
        var email = SurroundingSpaces + EmailOfLength(MaxEmailLength, 'm') + SurroundingSpaces;

        var response = await RegisterAsync(email);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task RegisterUser_TrimmedEmailLongerThanMax_Returns400()
    {
        var email = SurroundingSpaces + EmailOfLength(MaxEmailLength + 1, 'l') + SurroundingSpaces;

        var response = await RegisterAsync(email);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private Task<HttpResponseMessage> RegisterAsync(string email) =>
        _client.PostAsJsonAsync(RegisterEndpoint, new { Email = email, Password });

    private static string EmailOfLength(int length, char localPart) =>
        new string(localPart, length - EmailDomain.Length) + EmailDomain;

    private async Task<string> StoredEmailAsync(Guid registrationRequestId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();
        return (await db.RegistrationRequests.SingleAsync(r => r.Id == registrationRequestId)).Email;
    }

    private record RegisterUserResponse(Guid RegistrationRequestId);
}
