using DataTrustHub.Domain.Users;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Model;
using DataTrustHub.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataTrustHub.Features._Shared.Seeding;

// Ensures the configured admin account exists at startup: creates it, or promotes an existing user.
// An existing user's password is never changed.
public sealed class AdminSeeder(
    IServiceScopeFactory scopeFactory,
    IPasswordHasher hasher,
    IOptions<AdminSeedOptions> options,
    ILogger<AdminSeeder> logger) : IHostedService
{
    private const string SkippedLog = "Admin seeding skipped: {Section} is not configured.";
    private const string CreatedLog = "Admin account {Email} created.";
    private const string PromotedLog = "User {Email} promoted to admin.";

    private readonly AdminSeedOptions _options = options.Value;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured)
        {
            logger.LogInformation(SkippedLog, AdminSeedOptions.SectionName);
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == _options.Email, cancellationToken);

        if (user is null) await CreateAdminAsync(db, cancellationToken);
        else PromoteToAdmin(user);

        await db.SaveChangesAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task CreateAdminAsync(DContext db, CancellationToken cancellationToken)
    {
        var admin = new DbUser
        {
            Id = Guid.NewGuid(),
            Email = _options.Email!,
            HashedPassword = hasher.HashPassword(_options.Password!),
            Role = UserRoles.Admin
        };
        await db.Users.AddAsync(admin, cancellationToken);
        logger.LogInformation(CreatedLog, admin.Email);
    }

    private void PromoteToAdmin(DbUser user)
    {
        if (user.Role == UserRoles.Admin) return;

        user.Role = UserRoles.Admin;
        logger.LogInformation(PromotedLog, user.Email);
    }
}
