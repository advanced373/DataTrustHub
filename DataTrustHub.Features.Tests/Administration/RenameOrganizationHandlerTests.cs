using DataTrustHub.Features.Administration.RenameOrganization;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.SharedKernel;
using Xunit;

namespace DataTrustHub.Features.Tests.Administration;

// #23 at the handler, built directly with fakes, so no database is involved: O1 looks only at the other
// organizations, and a save the unique index rejects (a concurrent rename to the same name) answers the same
// conflict as the check
public class RenameOrganizationHandlerTests
{
    private const string OldName = "Acme";
    private const string NewName = "Acme Renamed";
    private const string OtherName = "Other organization";

    private readonly FakeOrganizationRepository _organizations = new();

    // --- AC2 ---

    [Fact]
    public async Task RenameOrganization_NameOfAnotherOrganization_FailsWithNameAlreadyInUseAndSavesNothing()
    {
        var organization = _organizations.Seed(OldName);
        _organizations.Seed(OtherName);
        var unitOfWork = new CountingUnitOfWork();

        var result = await new RenameOrganizationHandler(_organizations, unitOfWork)
            .Handle(new RenameOrganizationCommand(organization.Id, OtherName), default);

        AssertConflict(result);
        Assert.Equal((OldName, 0), (organization.Name, unitOfWork.SaveChangesCalls));
    }

    // --- AC3 ---

    [Fact]
    public async Task RenameOrganization_ToItsCurrentName_SucceedsAndChangesNothing()
    {
        var organization = _organizations.Seed(OldName);
        var unitOfWork = new CountingUnitOfWork();

        var result = await new RenameOrganizationHandler(_organizations, unitOfWork)
            .Handle(new RenameOrganizationCommand(organization.Id, OldName), default);

        Assert.True(result.IsSuccess);
        Assert.Equal((OldName, 0), (organization.Name, unitOfWork.SaveChangesCalls));
        Assert.Empty(_organizations.Updated);
    }

    // --- AC4 ---

    [Fact]
    public async Task RenameOrganization_UnknownOrganization_FailsWithNotFound()
    {
        var result = await new RenameOrganizationHandler(_organizations, new CountingUnitOfWork())
            .Handle(new RenameOrganizationCommand(Guid.NewGuid(), NewName), default);

        Assert.True(result.IsFailure);
        Assert.Equal((ErrorType.NotFound, AdminErrorCodes.OrganizationNotFound), (result.Error.Type, result.Error.Code));
    }

    // --- AC1 and AC9: renamed, saved once, the id kept ---

    [Fact]
    public async Task RenameOrganization_NewName_RenamesSavesOnceAndKeepsTheId()
    {
        var organization = _organizations.Seed(OldName);
        var id = organization.Id;
        var unitOfWork = new CountingUnitOfWork();

        var result = await new RenameOrganizationHandler(_organizations, unitOfWork)
            .Handle(new RenameOrganizationCommand(id, NewName), default);

        Assert.True(result.IsSuccess);
        Assert.Equal((id, NewName, 1), (organization.Id, organization.Name, unitOfWork.SaveChangesCalls));
        Assert.Equal(id, Assert.Single(_organizations.Updated).Id);
    }

    // --- AC7: two concurrent renames to the same name ---

    [Fact]
    public async Task RenameOrganization_UniqueIndexRejectsSave_FailsWithNameAlreadyInUseConflict()
    {
        var organization = _organizations.Seed(OldName);
        var unitOfWork = new UniqueViolationUnitOfWork();

        var result = await new RenameOrganizationHandler(_organizations, unitOfWork)
            .Handle(new RenameOrganizationCommand(organization.Id, NewName), default);

        AssertConflict(result);
        Assert.Equal(1, unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task RenameOrganization_UniqueIndexRejectsSave_FailsWithTheSameErrorAsTheCheck()
    {
        var organization = _organizations.Seed(OldName);
        var fromIndex = await new RenameOrganizationHandler(_organizations, new UniqueViolationUnitOfWork())
            .Handle(new RenameOrganizationCommand(organization.Id, NewName), default);
        var renamed = _organizations.Seed(OtherName);
        var fromCheck = await new RenameOrganizationHandler(_organizations, new CountingUnitOfWork())
            .Handle(new RenameOrganizationCommand(renamed.Id, NewName), default);

        Assert.Equal(fromCheck.Error, fromIndex.Error);
    }

    [Fact]
    public async Task RenameOrganization_OtherSaveFailure_IsNotTurnedIntoAConflict()
    {
        var organization = _organizations.Seed(OldName);
        var handler = new RenameOrganizationHandler(_organizations, new FailingUnitOfWork());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new RenameOrganizationCommand(organization.Id, NewName), default));
    }

    // --- AC10: the O1 check looks only at the other organizations ---

    [Fact]
    public async Task RenameOrganization_NewLetterCaseOfItsOwnName_SucceedsAndStoresIt()
    {
        const string upperCase = "ACME";
        var organization = _organizations.Seed(OldName);

        var result = await new RenameOrganizationHandler(_organizations, new CountingUnitOfWork())
            .Handle(new RenameOrganizationCommand(organization.Id, upperCase), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(upperCase, organization.Name);
    }

    // --- helpers ---

    private static void AssertConflict(Result<Guid> result)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(
            (ErrorType.Conflict, AdminErrorCodes.OrganizationNameAlreadyInUse, AdminErrorMessages.OrganizationNameAlreadyInUse),
            (result.Error.Type, result.Error.Code, result.Error.Description));
    }
}
