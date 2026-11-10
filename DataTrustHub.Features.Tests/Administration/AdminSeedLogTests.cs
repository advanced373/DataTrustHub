using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Users;
using DataTrustHub.Features._Shared.Seeding;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace DataTrustHub.Features.Tests.Administration;

// #170: email addresses are personal data, so the admin seeding never writes the admin's email to the logs:
// not when it creates the admin (AC1), not when it promotes an existing user (AC2), and not when the seed
// fails with a database exception whose message quotes the email (AC3). Otherwise the seeder behaves as
// before (AC4; the create, promote, password and not-configured behaviour is in AdminSeedTests)
public class AdminSeedLogTests
{
    private const string AdminEmail = "seeded-admin@example.com";
    private const string VariantAdminEmail = " Seeded-Admin@EXAMPLE.com ";
    private const string AdminPassword = "SeededPassword1!";
    private const string CreatedWord = "created";
    private const string PromotedWord = "promoted";
    private const string ExceptionTypePlaceholder = "<exception type>";

    // What SQL Server says when another instance saved the admin first: the message quotes the email
    private const string DuplicateKeyMessage =
        "Cannot insert duplicate key row in object 'dbo.Users' with unique index 'IX_Users_Email'. " +
        "The duplicate key value is (" + AdminEmail + ").";
    private const string SaveFailedMessage =
        "An error occurred while saving the entity changes for the user " + AdminEmail + ".";

    public enum SaveFailure
    {
        // EF Core's own exception, with the database's duplicate-key message inside
        DuplicateKeyUpdate,
        // What DContext throws for it: the domain's exception, wrapping EF Core's one
        UniqueConstraintViolation
    }

    // --- AC1: creating the admin ---

    [Fact]
    public async Task Seeder_CreatesAdmin_LogsThatTheAdminAccountWasCreated()
    {
        var fakes = new SeederFakes();

        await fakes.RunSeederAsync(AdminEmail);

        Assert.Single(fakes.Users.Added);
        Assert.Contains(fakes.Logger.Entries, entry =>
            entry.Level == LogLevel.Information
            && entry.Message.Contains(CreatedWord, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Seeder_CreatesAdmin_LogsNoEmailAddress()
    {
        var fakes = new SeederFakes();

        await fakes.RunSeederAsync(AdminEmail);

        AssertLogsNoEmail(fakes.Logger, AdminEmail);
    }

    [Fact]
    public async Task Seeder_CreatesAdminFromVariantEmail_LogsNeitherTheConfiguredNorTheNormalizedEmail()
    {
        var fakes = new SeederFakes();

        await fakes.RunSeederAsync(VariantAdminEmail);

        Assert.Single(fakes.Users.Added);
        AssertLogsNoEmail(fakes.Logger, AdminEmail);
        AssertLogsNoEmail(fakes.Logger, VariantAdminEmail.Trim());
    }

    // --- AC2: promoting an existing user ---

    [Fact]
    public async Task Seeder_PromotesExistingUser_LogsThatTheUserWasPromoted()
    {
        var fakes = new SeederFakes();
        fakes.Users.Seed(AdminEmail);

        await fakes.RunSeederAsync(AdminEmail);

        Assert.Single(fakes.Users.Updated);
        Assert.Contains(fakes.Logger.Entries, entry =>
            entry.Level == LogLevel.Information
            && entry.Message.Contains(PromotedWord, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Seeder_PromotesExistingUser_LogsNoEmailAddress()
    {
        var fakes = new SeederFakes();
        fakes.Users.Seed(AdminEmail);

        await fakes.RunSeederAsync(AdminEmail);

        AssertLogsNoEmail(fakes.Logger, AdminEmail);
    }

    // --- AC3: a failed seed, e.g. two instances seeding at once ---

    [Theory]
    [InlineData(SaveFailure.DuplicateKeyUpdate)]
    [InlineData(SaveFailure.UniqueConstraintViolation)]
    public async Task Seeder_SaveFailsQuotingTheEmail_LogsAnError(SaveFailure failure)
    {
        var fakes = new SeederFakes(new ThrowingUnitOfWork(FailureOf(failure)));

        await fakes.RunSeederAsync(AdminEmail);

        Assert.Contains(fakes.Logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    [Theory]
    [InlineData(SaveFailure.DuplicateKeyUpdate)]
    [InlineData(SaveFailure.UniqueConstraintViolation)]
    public async Task Seeder_SaveFailsQuotingTheEmail_AttachesNoExceptionToAnyLog(SaveFailure failure)
    {
        var fakes = new SeederFakes(new ThrowingUnitOfWork(FailureOf(failure)));

        await fakes.RunSeederAsync(AdminEmail);

        Assert.All(fakes.Logger.Entries, entry => Assert.Null(entry.Exception));
        Assert.All(fakes.Logger.Entries, entry =>
            Assert.DoesNotContain(entry.Values, value => value.Value is Exception));
    }

    [Theory]
    [InlineData(SaveFailure.DuplicateKeyUpdate)]
    [InlineData(SaveFailure.UniqueConstraintViolation)]
    public async Task Seeder_SaveFailsQuotingTheEmail_LogsNoExceptionMessage(SaveFailure failure)
    {
        var exception = FailureOf(failure);
        var fakes = new SeederFakes(new ThrowingUnitOfWork(exception));

        await fakes.RunSeederAsync(AdminEmail);

        foreach (var message in MessagesOf(exception))
            AssertLogsNoText(fakes.Logger, message);
    }

    [Theory]
    [InlineData(SaveFailure.DuplicateKeyUpdate)]
    [InlineData(SaveFailure.UniqueConstraintViolation)]
    public async Task Seeder_SaveFailsQuotingTheEmail_LogsNoEmailAddress(SaveFailure failure)
    {
        var fakes = new SeederFakes(new ThrowingUnitOfWork(FailureOf(failure)));

        await fakes.RunSeederAsync(AdminEmail);

        AssertLogsNoEmail(fakes.Logger, AdminEmail);
    }

    // A fixed message: two different failures log the same error, apart from the exception type it may name
    [Fact]
    public async Task Seeder_DifferentFailures_LogTheSameFixedErrorMessage()
    {
        var duplicateKey = FailureOf(SaveFailure.DuplicateKeyUpdate);
        var unreachable = UnreachableDatabase.Failure();

        var duplicateKeyError = await SingleErrorMessageAsync(duplicateKey);
        var unreachableError = await SingleErrorMessageAsync(unreachable);

        Assert.Equal(
            WithoutExceptionType(duplicateKeyError, duplicateKey),
            WithoutExceptionType(unreachableError, unreachable));
    }

    // --- AC4: otherwise unchanged ---

    [Theory]
    [InlineData(SaveFailure.DuplicateKeyUpdate)]
    [InlineData(SaveFailure.UniqueConstraintViolation)]
    public async Task Seeder_SaveFailsQuotingTheEmail_DoesNotThrow(SaveFailure failure)
    {
        var fakes = new SeederFakes(new ThrowingUnitOfWork(FailureOf(failure)));

        var exception = await Record.ExceptionAsync(() => fakes.RunSeederAsync(AdminEmail));

        Assert.Null(exception);
    }

    [Fact]
    public async Task Seeder_SaveCanceled_LetsTheCancellationThrough()
    {
        var fakes = new SeederFakes(new ThrowingUnitOfWork(new OperationCanceledException()));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fakes.RunSeederAsync(AdminEmail));
    }

    [Fact]
    public async Task Seeder_SaveCanceled_LogsNoError()
    {
        var fakes = new SeederFakes(new ThrowingUnitOfWork(new OperationCanceledException()));

        await Record.ExceptionAsync(() => fakes.RunSeederAsync(AdminEmail));

        Assert.DoesNotContain(fakes.Logger.Entries, entry => entry.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task Seeder_EmailConfiguredWithoutPassword_DoesNothingAndLogsNoEmailAddress()
    {
        var fakes = new SeederFakes();

        await fakes.RunSeederAsync(AdminEmail, password: string.Empty);

        Assert.Empty(fakes.Users.Added);
        Assert.Equal(0, fakes.CountingUnitOfWork.SaveChangesCalls);
        AssertLogsNoEmail(fakes.Logger, AdminEmail);
    }

    // --- The real host: the startup seed writes no email through the configured logging ---

    [Fact]
    public async Task Startup_SeederCreatesAdmin_WritesNoEmailAddressToTheHostLogs()
    {
        await using var factory = new LoggedSeedApiFactory();
        factory.CreateClient();
        await SeederRuns.WaitForStartupSeedAsync(factory.Services);

        var seederLines = factory.Logs.Lines
            .Where(line => line.StartsWith(typeof(AdminSeeder).FullName!, StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(seederLines);
        Assert.All(seederLines, line => Assert.DoesNotContain(AdminEmail, line, StringComparison.OrdinalIgnoreCase));
    }

    private static Exception FailureOf(SaveFailure failure)
    {
        var updateFailure = new DbUpdateException(SaveFailedMessage, new InvalidOperationException(DuplicateKeyMessage));
        return failure == SaveFailure.DuplicateKeyUpdate
            ? updateFailure
            : new UniqueConstraintViolationException(updateFailure);
    }

    private static IEnumerable<string> MessagesOf(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
            yield return current.Message;
    }

    private static async Task<string> SingleErrorMessageAsync(Exception failure)
    {
        var fakes = new SeederFakes(new ThrowingUnitOfWork(failure));
        await fakes.RunSeederAsync(AdminEmail);
        return Assert.Single(fakes.Logger.Entries, entry => entry.Level == LogLevel.Error).Message;
    }

    private static string WithoutExceptionType(string message, Exception exception) =>
        message
            .Replace(exception.GetType().FullName!, ExceptionTypePlaceholder, StringComparison.Ordinal)
            .Replace(exception.GetType().Name, ExceptionTypePlaceholder, StringComparison.Ordinal);

    private static void AssertLogsNoEmail(CapturingLogger<AdminSeeder> logger, string email) =>
        AssertLogsNoText(logger, email);

    // Nothing logged may carry the text: the formatted message, any structured value (the template included),
    // the attached exception (message, inner exceptions, stack trace) or a scope
    private static void AssertLogsNoText(CapturingLogger<AdminSeeder> logger, string text)
    {
        Assert.All(logger.Entries, entry =>
        {
            Assert.DoesNotContain(text, entry.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(text, entry.Exception?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.All(entry.Values, value =>
                Assert.DoesNotContain(text, $"{value.Key}={value.Value}", StringComparison.OrdinalIgnoreCase));
        });
        Assert.All(logger.Scopes, scope => Assert.DoesNotContain(text, scope, StringComparison.OrdinalIgnoreCase));
    }

    // A save that fails with the given exception, as DContext or EF Core throws it
    private sealed class ThrowingUnitOfWork(Exception failure) : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => throw failure;
    }

    // The seeder resolves the repository and unit of work from its own scope, so the fakes are served by
    // a small container; singletons make every scope see the same instances
    private sealed class SeederFakes
    {
        private readonly IServiceProvider provider;

        internal SeederFakes(IUnitOfWork? unitOfWork = null)
        {
            provider = new ServiceCollection()
                .AddSingleton<IUserRepository>(Users)
                .AddSingleton(unitOfWork ?? CountingUnitOfWork)
                .BuildServiceProvider();
        }

        internal FakeUserRepository Users { get; } = new();
        internal CountingUnitOfWork CountingUnitOfWork { get; } = new();
        internal CapturingLogger<AdminSeeder> Logger { get; } = new();

        internal Task RunSeederAsync(string email, string password = AdminPassword) =>
            new AdminSeeder(
                    provider.GetRequiredService<IServiceScopeFactory>(),
                    new FakePasswordHasher(),
                    Options.Create(new AdminSeedOptions { Email = email, Password = password }),
                    Logger)
                .RunToCompletionAsync();
    }

    // The real API host with the admin seed configured and every log entry captured
    private sealed class LoggedSeedApiFactory : ApiFactory
    {
        internal CapturedLogs Logs { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            OverrideSettings(builder, new Dictionary<string, string?>
            {
                [$"{AdminSeedOptions.SectionName}:Email"] = AdminEmail,
                [$"{AdminSeedOptions.SectionName}:Password"] = AdminPassword
            });
            builder.ConfigureLogging(Logs.AddTo);
        }
    }
}
