using System.Net;
using System.Net.Http.Json;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataTrustHub.Features.Tests.Administration;

// AC3 and AC7 of #123: the platform admin's create account stores the normalized email (U7), a case or space
// variant of an existing address is refused (U1), and the format and length rules (U2) apply to the trimmed address
public class CreateAccountEmailNormalizationTests(AdminApiFactory factory) : IClassFixture<AdminApiFactory>
{
    private const string CreateAccountEndpoint = "/admin/accounts";
    private const string Password = "Password1!";
    private const string EmailDomain = "@example.com";
    private const int MaxEmailLength = 256;
    private const string SurroundingSpaces = "   ";

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task CreateAccount_EmailWithCaseAndSurroundingSpaces_StoresTheNormalizedEmail()
    {
        var response = await CreateAccountAsync(" Acc.Norm.Stored@Example.COM ");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var userId = (await response.Content.ReadFromJsonAsync<CreateAccountResponse>())!.UserId;
        Assert.Equal("acc.norm.stored@example.com", await StoredEmailAsync(userId));
    }

    // The variants themselves are pinned on EmailAddress (EmailAddressTests); here one variant each way:
    // the request is a variant, and the existing address was created as one
    [Theory]
    [InlineData("acc_dup_variant@example.com", "\tAcc_Dup_Variant@Example.Com ")]
    [InlineData(" Acc_Dup_Reverse@Example.COM ", "acc_dup_reverse@example.com")]
    public async Task CreateAccount_VariantOfAnExistingEmail_Returns409AccountEmailAlreadyInUse(string existing, string variant)
    {
        await CreateAccountAsync(existing);

        var response = await CreateAccountAsync(variant);

        await ErrorAssert.HasErrorAsync(
            response, HttpStatusCode.Conflict, AdminErrorCodes.AccountEmailAlreadyInUse, AdminErrorMessages.AccountEmailAlreadyInUse);
    }

    [Fact]
    public async Task CreateAccount_VariantOfAnExistingEmail_StoresNoSecondUser()
    {
        await CreateAccountAsync("acc_single@example.com");

        await CreateAccountAsync(" ACC_Single@Example.com ");

        Assert.Equal(1, await CountUsersAsync("acc_single@example.com"));
    }

    // --- AC7: validation on the trimmed address ---

    [Theory]
    [InlineData("")]
    [InlineData(" \t ")]
    public async Task CreateAccount_EmptyOrWhitespaceOnlyEmail_Returns400(string email)
    {
        var response = await CreateAccountAsync(email);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateAccount_MalformedEmailWithSurroundingSpaces_Returns400()
    {
        var response = await CreateAccountAsync(" not-an-email ");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateAccount_TrimmedEmailOfMaxLengthWithSurroundingSpaces_Returns200()
    {
        var email = SurroundingSpaces + EmailOfLength(MaxEmailLength, 'm') + SurroundingSpaces;

        var response = await CreateAccountAsync(email);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CreateAccount_TrimmedEmailLongerThanMax_Returns400()
    {
        var email = SurroundingSpaces + EmailOfLength(MaxEmailLength + 1, 'l') + SurroundingSpaces;

        var response = await CreateAccountAsync(email);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private Task<HttpResponseMessage> CreateAccountAsync(string email) =>
        _client.PostAsJsonAsync(CreateAccountEndpoint, new { Email = email, Password });

    private static string EmailOfLength(int length, char localPart) =>
        new string(localPart, length - EmailDomain.Length) + EmailDomain;

    private async Task<string> StoredEmailAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();
        return (await db.Users.SingleAsync(u => u.Id == userId)).Email;
    }

    private async Task<int> CountUsersAsync(string storedEmail)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();
        return await db.Users.CountAsync(u => u.Email == storedEmail);
    }

    private record CreateAccountResponse(Guid UserId);
}
