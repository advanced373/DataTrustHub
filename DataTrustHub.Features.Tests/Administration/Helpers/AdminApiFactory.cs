using DataTrustHub.Infrastructure.Persistance;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace DataTrustHub.Features.Tests.Administration.Helpers;

/// <summary>
/// Factory that configures an in-memory DB and a fake authenticated user for admin endpoint tests.
/// </summary>
public class AdminApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            RemoveDbContextRegistrations(services);

            var dbName = Guid.NewGuid().ToString();
            services.AddDbContext<DContext>(options =>
                options.UseInMemoryDatabase(dbName));

            RegisterFakeAuthentication(services);
        });
    }

    private static void RemoveDbContextRegistrations(IServiceCollection services)
    {
        var descriptorsToRemove = services
            .Where(d =>
                d.ServiceType == typeof(DbContextOptions<DContext>) ||
                d.ServiceType == typeof(DbContextOptions) ||
                d.ServiceType == typeof(DContext) ||
                d.ServiceType == typeof(IDbContextOptionsConfiguration<DContext>))
            .ToList();

        foreach (var descriptor in descriptorsToRemove)
            services.Remove(descriptor);
    }

    private static void RegisterFakeAuthentication(IServiceCollection services)
    {
        services.AddAuthentication(FakeAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, FakeAuthHandler>(
                FakeAuthHandler.SchemeName, _ => { });
    }
}

/// <summary>
/// Authentication handler that always authenticates requests as an admin user.
/// Used only in integration tests to bypass JWT validation.
/// </summary>
internal class FakeAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    internal const string SchemeName = "FakeAuth";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, "admin@test.com"),
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, "Admin")
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
