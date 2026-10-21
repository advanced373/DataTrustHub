using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Users;
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

        // Hosted services are singletons, so the scoped repository and unit of work need their own scope
        using var scope = scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var user = await users.GetByEmailAsync(_options.Email!, cancellationToken);

        var changed = user is null
            ? CreateAdmin(users)
            : await PromoteToAdminAsync(users, user, cancellationToken);

        if (changed)
            await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private bool CreateAdmin(IUserRepository users)
    {
        var admin = User.CreateAdmin(_options.Email!, hasher.HashPassword(_options.Password!));
        users.Add(admin);
        logger.LogInformation(CreatedLog, admin.Email);
        return true;
    }

    // Returns false when the user already was an admin, so there is nothing to save
    private async Task<bool> PromoteToAdminAsync(
        IUserRepository users, User user, CancellationToken cancellationToken)
    {
        if (!user.PromoteToAdmin()) return false;

        await users.UpdateAsync(user, cancellationToken);
        logger.LogInformation(PromotedLog, user.Email);
        return true;
    }
}
