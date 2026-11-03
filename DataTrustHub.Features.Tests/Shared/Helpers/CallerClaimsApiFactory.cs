using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Encodings.Web;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.SharedKernel;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataTrustHub.Features.Tests.Shared.Helpers;

/// <summary>
/// Authenticates every request as a platform admin with the caller id claims the subclass gives, so a
/// role-guarded route reaches the endpoint and its own caller id check.
/// </summary>
public abstract class CallerClaimsApiFactory : ApiFactory
{
    protected abstract IReadOnlyList<Claim> CallerIdClaims { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
            services.AddAuthentication(CallerClaimsAuthHandler.SchemeName)
                .AddScheme<CallerClaimsAuthOptions, CallerClaimsAuthHandler>(
                    CallerClaimsAuthHandler.SchemeName, options => options.CallerIdClaims = CallerIdClaims));
    }
}

/// <summary>An authenticated caller with neither a <c>sub</c> nor a name identifier claim.</summary>
public sealed class MissingCallerIdApiFactory : CallerClaimsApiFactory
{
    protected override IReadOnlyList<Claim> CallerIdClaims => [];
}

/// <summary>
/// An authenticated caller whose <c>sub</c> is not a Guid while the name identifier is one: the caller id
/// is malformed, the name identifier is no fallback for a <c>sub</c> that is present.
/// </summary>
public sealed class MalformedCallerIdApiFactory : CallerClaimsApiFactory
{
    internal const string NonGuidSubject = "not-a-guid";

    protected override IReadOnlyList<Claim> CallerIdClaims =>
    [
        new Claim(JwtRegisteredClaimNames.Sub, NonGuidSubject),
        new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())
    ];
}

public sealed class CallerClaimsAuthOptions : AuthenticationSchemeOptions
{
    public IReadOnlyList<Claim> CallerIdClaims { get; set; } = [];
}

internal sealed class CallerClaimsAuthHandler(
    IOptionsMonitor<CallerClaimsAuthOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<CallerClaimsAuthOptions>(options, logger, encoder)
{
    internal const string SchemeName = "CallerClaims";
    private const string Name = "callerclaims@example.com";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        Claim[] claims = [new Claim(ClaimTypes.Name, Name), new Claim(ClaimTypes.Role, UserRoles.Admin), .. Options.CallerIdClaims];
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
