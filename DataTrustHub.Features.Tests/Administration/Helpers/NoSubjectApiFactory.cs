using System.Security.Claims;
using System.Text.Encodings.Web;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.SharedKernel;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataTrustHub.Features.Tests.Administration.Helpers;

/// <summary>
/// Authenticates every request as a platform admin whose identity has no subject (no <c>sub</c> and no
/// name identifier claim), so endpoints that read the caller id must answer 401 themselves.
/// </summary>
public sealed class NoSubjectApiFactory : ApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
            services.AddAuthentication(NoSubjectAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, NoSubjectAuthHandler>(NoSubjectAuthHandler.SchemeName, _ => { }));
    }
}

internal sealed class NoSubjectAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    internal const string SchemeName = "NoSubject";
    private const string Name = "nosubject@example.com";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // A platform admin, so role-guarded routes reach the endpoint and its own caller id check
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, Name), new Claim(ClaimTypes.Role, UserRoles.Admin)], SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
