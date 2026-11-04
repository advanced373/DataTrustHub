using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DataTrustHub.Features.Tests.WebApp.Helpers;

// Browses the WebApp like a user: signs in through the login page (the stub API issues the token), reads
// antiforgery tokens from the pages and posts forms with them
internal static partial class WebAppPages
{
    internal const string LoginPath = "/Account/Login";
    internal const string RegisterPath = "/Account/Register";
    internal const string AccessDeniedPath = "/Account/AccessDenied";
    internal const string HomePath = "/";
    internal const string AdminPath = "/Admin";
    internal const string UsersPath = "/Admin/Users";
    internal const string AuditPath = "/Admin/Audit";
    internal const string RegistrationRequestsPath = "/Admin/RegistrationRequests";
    internal const string AntiforgeryField = "__RequestVerificationToken";
    internal const string AuthenticationCookie = ".AspNetCore.Cookies";

    // The API's routes the WebApp calls, relative to its base address
    internal const string ApiLogin = "auth/login";
    internal const string ApiRegister = "auth/register";
    internal const string ApiAccounts = "admin/accounts";
    internal const string ApiAuditRecords = "admin/audit-records";
    internal const string ApiRegistrationRequests = "admin/registration-requests";

    // The role names the API puts in the JWT's "role" claim
    internal const string AdminRole = "Admin";
    internal const string UserRole = "User";
    private const string JwtRoleClaim = "role";
    private const string SignInPassword = "Password1!";

    internal static HttpClient CreateClient(StubbedApiWebAppFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    internal static string ApiApprove(Guid id) => $"admin/registration-requests/{id}/approve";

    internal static string ApiReject(Guid id) => $"admin/registration-requests/{id}/reject";

    internal static string ApprovePath(Guid id) => $"{RegistrationRequestsPath}/{id}/Approve";

    internal static string RejectPath(Guid id) => $"{RegistrationRequestsPath}/{id}/Reject";

    // Signs the client in with the given role; the stub API answers the login with a token carrying it
    internal static async Task SignInAsync(StubbedApiWebAppFactory factory, HttpClient client, string role)
    {
        var email = $"{role.ToLowerInvariant()}@datatrusthub.test";
        var token = CreateToken(email, role);
        factory.Api.Answer(HttpMethod.Post, ApiLogin, HttpStatusCode.OK, JsonSerializer.Serialize(new { token }));

        var response = await PostFormAsync(client, LoginPath, LoginPath, new Dictionary<string, string>
        {
            ["Email"] = email,
            ["Password"] = SignInPassword
        });
        if (response.StatusCode != HttpStatusCode.Redirect)
            throw new InvalidOperationException($"Sign-in failed with {response.StatusCode}.");
    }

    // Reads the antiforgery token from the form page, then posts the fields with it
    internal static async Task<HttpResponseMessage> PostFormAsync(
        HttpClient client, string formPagePath, string postPath, IDictionary<string, string> fields)
    {
        var page = await client.GetStringAsync(formPagePath);
        var content = new Dictionary<string, string>(fields) { [AntiforgeryField] = AntiforgeryTokenOf(page) };
        return await client.PostAsync(postPath, new FormUrlEncodedContent(content));
    }

    // Follows one redirect and returns the page's decoded HTML
    internal static async Task<string> FollowAsync(HttpClient client, HttpResponseMessage redirect) =>
        WebUtility.HtmlDecode(await client.GetStringAsync(redirect.Headers.Location));

    internal static async Task<string> GetPageAsync(HttpClient client, string path) =>
        WebUtility.HtmlDecode(await client.GetStringAsync(path));

    internal static string AntiforgeryTokenOf(string html)
    {
        var match = AntiforgeryInput().Match(html);
        if (!match.Success) throw new InvalidOperationException("The page has no antiforgery token.");
        return match.Groups["token"].Value;
    }

    // Every <form> of a page: its opening tag and its inner HTML
    internal static IReadOnlyList<(string Tag, string Inner)> FormsOf(string html) =>
        FormElement().Matches(html).Select(match => (match.Groups["tag"].Value, match.Groups["inner"].Value)).ToList();

    private static string CreateToken(string email, string role)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim(JwtRoleClaim, role)
        };
        // The WebApp only reads the token (the API validates it), so it needs no signature here
        var jwt = new JwtSecurityToken(claims: claims, expires: DateTime.UtcNow.AddHours(1));
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"(?<token>[^\"]+)\"")]
    private static partial Regex AntiforgeryInput();

    [GeneratedRegex("(?<tag><form[^>]*>)(?<inner>.*?)</form>", RegexOptions.Singleline)]
    private static partial Regex FormElement();
}
