using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Users;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataTrustHub.Features._Shared.Seeding;

// Ensures the configured admin account exists at startup: creates it, or promotes an existing user.
// An existing user's password is never changed.
// Runs as a background service so the host starts listening (and /health answers) while the seed waits
// for a database that is still resuming or unreachable (ADR-0021).
public sealed class AdminSeeder(
    IServiceScopeFactory scopeFactory,
    IPasswordHasher hasher,
    IOptions<AdminSeedOptions> options,
    ILogger<AdminSeeder> logger) : BackgroundService
{
    private const string SkippedLog = "Admin seeding skipped: {Section} is not configured.";
    private const string CreatedLog = "Admin account {Email} created.";
    private const string PromotedLog = "User {Email} promoted to admin.";
    private const string FailedLog = "Admin seeding failed; it is tried again at the next start.";

    private readonly AdminSeedOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Yield first, so the host's StartAsync does not wait for the seed and the server starts listening
        await Task.Yield();

        if (!_options.IsConfigured)
        {
            logger.LogInformation(SkippedLog, AdminSeedOptions.SectionName);
            return;
        }

        // An unreachable database (e.g. a paused serverless database, ADR-0021) must not stop the application;
        // the exception carries no admin password and the log message adds no secret
        try
        {
            await SeedAsync(stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, FailedLog);
        }
    }

    private async Task SeedAsync(CancellationToken cancellationToken)
    {
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
