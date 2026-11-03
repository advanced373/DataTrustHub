using System.Net;
using System.Net.Http.Json;
using DataTrustHub.Features.Tests.Authentication.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataTrustHub.Features.Tests.Auditing;

// #24 AC7 and #172 AC8: register is still audited as RegisterUser, anonymous, with its outcome and error code,
// and the record holds neither the email nor the password. On SQLite (RegistrationApiFactory), since register
// purges expired registration requests with a set-based delete. Tests in a class run one after another, so
// "the records added by this request" is exact
public class RegisterUserAuditTests(RegistrationApiFactory factory) : IClassFixture<RegistrationApiFactory>
{
    private const string RegisterEndpoint = "/auth/register";
    private const string Password = "Password1!";
    private const string InvalidEmail = "not-an-email";
    private const string ShortPassword = "short";

    private const string RegisterUserAction = "RegisterUser";
    private const string SucceededOutcome = "Succeeded";
    private const string RejectedOutcome = "Rejected";
    private const string ValidationCode = "Validation.General";

    [Fact]
    public async Task Register_Succeeded_RecordsAnonymousWithoutTargetOrganizationOrEmail()
    {
        var email = NewEmail("audit_register");

        var records = await RecordsAddedByAsync(() => RegisterAsync(email));

        var record = Assert.Single(records);
        AssertRecord(record, SucceededOutcome, null);
        Assert.Equal(((string?)null, (Guid?)null, (Guid?)null), (record.TargetType, record.TargetId, record.OrganizationId));
        AssertHoldsNo(record, email);
        AssertHoldsNo(record, Password);
    }

    [Fact]
    public async Task Register_EmailInUse_RecordsRejectedWithTheErrorCodeAndNoEmail()
    {
        var email = NewEmail("audit_register_taken");
        await TestUsers.CreateAsync(factory, email, Password);

        var records = await RecordsAddedByAsync(() => RegisterAsync(email));

        var record = Assert.Single(records);
        AssertRecord(record, RejectedOutcome, AuthErrorCodes.EmailAlreadyInUse);
        AssertHoldsNo(record, email);
    }

    [Fact]
    public async Task Register_EmailPending_RecordsRejectedWithAlreadyPendingAndNoEmail()
    {
        var email = NewEmail("audit_register_pending");
        await RegisterAsync(email);

        var records = await RecordsAddedByAsync(() => RegisterAsync(email));

        var record = Assert.Single(records);
        AssertRecord(record, RejectedOutcome, AuthErrorCodes.RegistrationAlreadyPending);
        AssertHoldsNo(record, email);
    }

    // The audit decorator wraps the validation decorator, so a request the validator refuses is recorded too
    [Fact]
    public async Task Register_InvalidInput_RecordsTheValidationRejection()
    {
        var records = await RecordsAddedByAsync(() =>
            factory.CreateClient().PostAsJsonAsync(RegisterEndpoint, new { Email = InvalidEmail, Password = ShortPassword }));

        var record = Assert.Single(records);
        AssertRecord(record, RejectedOutcome, ValidationCode);
        AssertHoldsNo(record, InvalidEmail);
    }

    [Fact]
    public async Task Register_InvalidInput_StillAnswers400()
    {
        var response = await factory.CreateClient().PostAsJsonAsync(RegisterEndpoint, new { Email = InvalidEmail, Password = ShortPassword });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --- helpers ---

    private static string NewEmail(string prefix) => $"{prefix}_{Guid.NewGuid():N}@example.com";

    private Task<HttpResponseMessage> RegisterAsync(string email) =>
        factory.CreateClient().PostAsJsonAsync(RegisterEndpoint, new { Email = email, Password });

    // The audit records a request added: the trail before it, compared with the trail after it
    private async Task<List<DbAuditRecord>> RecordsAddedByAsync(Func<Task<HttpResponseMessage>> request)
    {
        var before = (await StoredRecordsAsync()).Select(r => r.Id).ToHashSet();
        await request();
        return (await StoredRecordsAsync()).Where(r => !before.Contains(r.Id)).ToList();
    }

    private async Task<List<DbAuditRecord>> StoredRecordsAsync()
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<DContext>().AuditRecords.AsNoTracking().ToListAsync();
    }

    private static void AssertRecord(DbAuditRecord record, string outcome, string? errorCode) =>
        Assert.Equal((RegisterUserAction, (Guid?)null, outcome, errorCode), (record.Action, record.ActorUserId, record.Outcome, record.ErrorCode));

    // No text column of the record holds the value (ADR-0016 point 3)
    private static void AssertHoldsNo(DbAuditRecord record, string value)
    {
        string?[] texts = [record.Action, record.TargetType, record.Outcome, record.ErrorCode];
        Assert.All(texts, text => Assert.DoesNotContain(value, text ?? string.Empty, StringComparison.OrdinalIgnoreCase));
    }
}
