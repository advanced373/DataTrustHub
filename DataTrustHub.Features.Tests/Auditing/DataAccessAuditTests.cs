using System.Net;
using System.Net.Http.Json;
using System.Text;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Auditing.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using Xunit;
using static DataTrustHub.Features.Tests.Administration.Helpers.MembershipSetup;
using static DataTrustHub.Features.Tests.Auditing.Helpers.DataAccessAuditSetup;

namespace DataTrustHub.Features.Tests.Auditing;

// #24 step 2 through the API: every download and every denied read of GET /data/{id} leaves a DataItem.Download
// audit record (ADR-0016 point 2); a missing item and the lists leave none; a denied read answers exactly like a
// missing item (ADR-0011). Tests in a class run one after another, so "the records added by this request" is exact
public class DataAccessAuditTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // --- AC1: a download is recorded ---

    [Fact]
    public async Task Download_Personal_RecordsSucceededWithTheActorTargetAndNoOrganization()
    {
        var (owner, ownerId) = await CreateUserAsync(factory, "dl_personal");
        var itemId = await UploadAsync(owner);

        var records = await RecordsAddedByAsync(factory, () => owner.GetAsync(ItemEndpoint(itemId)));

        AssertRecord(Assert.Single(records), ownerId, itemId, null, Succeeded, null);
    }

    [Fact]
    public async Task Download_Classified_RecordsSucceededWithThePolicysOrganization()
    {
        var scenario = await ArrangeClassifiedAsync(factory, "dl_classified");

        var records = await RecordsAddedByAsync(factory, () => scenario.Owner.GetAsync(ItemEndpoint(scenario.ItemId)));

        AssertRecord(Assert.Single(records), scenario.OwnerId, scenario.ItemId, scenario.OrganizationId, Succeeded, null);
    }

    [Fact]
    public async Task Download_ByClearedReader_RecordsTheReaderAsActor()
    {
        var scenario = await ArrangeClassifiedAsync(factory, "dl_reader");
        var (reader, readerId) = await CreateReaderAsync(factory, scenario, "dl_reader_reader", Confidential);

        var records = await RecordsAddedByAsync(factory, () => reader.GetAsync(ItemEndpoint(scenario.ItemId)));

        AssertRecord(Assert.Single(records), readerId, scenario.ItemId, scenario.OrganizationId, Succeeded, null);
    }

    [Fact]
    public async Task Download_StillReturnsTheContent()
    {
        var (owner, _) = await CreateUserAsync(factory, "dl_content");
        var itemId = await UploadAsync(owner);

        var response = await owner.GetAsync(ItemEndpoint(itemId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = await response.Content.ReadFromJsonAsync<DownloadResponse>();
        Assert.Equal(FileContent, Encoding.UTF8.GetString(Convert.FromBase64String(item!.Content!)));
    }

    // A2: the record never holds the file content, neither as text nor as the base64 the response carries
    [Fact]
    public async Task Download_RecordHoldsNoContent()
    {
        var (owner, _) = await CreateUserAsync(factory, "dl_no_content");
        var itemId = await UploadAsync(owner);

        var records = await RecordsAddedByAsync(factory, () => owner.GetAsync(ItemEndpoint(itemId)));

        var record = Assert.Single(records);
        AssertHoldsNo(record, FileContent);
        AssertHoldsNo(record, Base64Content);
    }

    // --- AC2: a denied read is recorded ---

    [Fact]
    public async Task DeniedRead_ClassifiedWithoutClearance_RecordsDeniedWithThePolicysOrganization()
    {
        var scenario = await ArrangeClassifiedAsync(factory, "deny_uncleared");
        var (intruder, intruderId) = await CreateUserAsync(factory, "deny_uncleared_intruder");

        var records = await RecordsAddedByAsync(factory, () => intruder.GetAsync(ItemEndpoint(scenario.ItemId)));

        AssertRecord(Assert.Single(records), intruderId, scenario.ItemId, scenario.OrganizationId, Denied, DataItemNotFoundCode);
    }

    [Fact]
    public async Task DeniedRead_ClearanceBelowLevel_RecordsDenied()
    {
        var scenario = await ArrangeClassifiedAsync(factory, "deny_below");
        // Qualified: since the .NET 10 package upgrade a namespace named Internal hides the static import
        var (reader, readerId) = await CreateReaderAsync(
            factory, scenario, "deny_below_reader", DataAccessAuditSetup.Internal);

        var records = await RecordsAddedByAsync(factory, () => reader.GetAsync(ItemEndpoint(scenario.ItemId)));

        AssertRecord(Assert.Single(records), readerId, scenario.ItemId, scenario.OrganizationId, Denied, DataItemNotFoundCode);
    }

    // Need-to-know: the Admin role grants no read, so its attempt is a denied read too
    [Fact]
    public async Task DeniedRead_AdminWithoutClearance_RecordsDenied()
    {
        var scenario = await ArrangeClassifiedAsync(factory, "deny_admin");
        var adminId = await PlatformAdminIdAsync(factory, "deny_admin");

        var records = await RecordsAddedByAsync(factory, () => scenario.PlatformAdmin.GetAsync(ItemEndpoint(scenario.ItemId)));

        AssertRecord(Assert.Single(records), adminId, scenario.ItemId, scenario.OrganizationId, Denied, DataItemNotFoundCode);
    }

    [Fact]
    public async Task DeniedRead_PersonalOfAnotherUser_RecordsDeniedWithoutOrganization()
    {
        var (owner, _) = await CreateUserAsync(factory, "deny_personal_owner");
        var itemId = await UploadAsync(owner);
        var (intruder, intruderId) = await CreateUserAsync(factory, "deny_personal_intruder");

        var records = await RecordsAddedByAsync(factory, () => intruder.GetAsync(ItemEndpoint(itemId)));

        AssertRecord(Assert.Single(records), intruderId, itemId, null, Denied, DataItemNotFoundCode);
    }

    [Fact]
    public async Task DeniedRead_OwnSoftDeletedPersonal_RecordsDenied()
    {
        var (owner, ownerId) = await CreateUserAsync(factory, "deny_deleted_personal");
        var itemId = await UploadAsync(owner);
        (await owner.DeleteAsync(ItemEndpoint(itemId))).EnsureSuccessStatusCode();

        var records = await RecordsAddedByAsync(factory, () => owner.GetAsync(ItemEndpoint(itemId)));

        AssertRecord(Assert.Single(records), ownerId, itemId, null, Denied, DataItemNotFoundCode);
    }

    [Fact]
    public async Task DeniedRead_SoftDeletedClassified_RecordsDeniedWithThePolicysOrganization()
    {
        var scenario = await ArrangeClassifiedAsync(factory, "deny_deleted_classified");
        (await scenario.Owner.DeleteAsync(ItemEndpoint(scenario.ItemId))).EnsureSuccessStatusCode();

        var records = await RecordsAddedByAsync(factory, () => scenario.Owner.GetAsync(ItemEndpoint(scenario.ItemId)));

        AssertRecord(Assert.Single(records), scenario.OwnerId, scenario.ItemId, scenario.OrganizationId, Denied, DataItemNotFoundCode);
    }

    // The organization is known only while the item's policy exists
    [Fact]
    public async Task DeniedRead_PolicyNoLongerExists_RecordsDeniedWithoutOrganization()
    {
        var scenario = await ArrangeClassifiedAsync(factory, "deny_no_policy");
        await RemovePolicyAsync(factory, scenario.PolicyId);
        var (intruder, intruderId) = await CreateUserAsync(factory, "deny_no_policy_intruder");

        var records = await RecordsAddedByAsync(factory, () => intruder.GetAsync(ItemEndpoint(scenario.ItemId)));

        AssertRecord(Assert.Single(records), intruderId, scenario.ItemId, null, Denied, DataItemNotFoundCode);
    }

    // ADR-0011: status and body of a denied read are those of a missing item, so existence is not revealed
    [Fact]
    public async Task DeniedRead_AnswersExactlyLikeAMissingItem()
    {
        var scenario = await ArrangeClassifiedAsync(factory, "deny_same_answer");
        var (intruder, _) = await CreateUserAsync(factory, "deny_same_answer_intruder");

        var denied = await intruder.GetAsync(ItemEndpoint(scenario.ItemId));
        var missing = await intruder.GetAsync(ItemEndpoint(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.Equal(missing.StatusCode, denied.StatusCode);
        Assert.Equal(await missing.Content.ReadAsStringAsync(), await denied.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DeniedRead_SoftDeleted_AnswersExactlyLikeAMissingItem()
    {
        var (owner, _) = await CreateUserAsync(factory, "deny_deleted_same_answer");
        var itemId = await UploadAsync(owner);
        (await owner.DeleteAsync(ItemEndpoint(itemId))).EnsureSuccessStatusCode();

        var denied = await owner.GetAsync(ItemEndpoint(itemId));
        var missing = await owner.GetAsync(ItemEndpoint(Guid.NewGuid()));

        Assert.Equal((HttpStatusCode.NotFound, await missing.Content.ReadAsStringAsync()),
            (denied.StatusCode, await denied.Content.ReadAsStringAsync()));
    }

    // --- AC3: a missing item is not an access ---

    [Fact]
    public async Task MissingItem_LeavesNoRecord()
    {
        var (client, _) = await CreateUserAsync(factory, "missing_item");

        var records = await RecordsAddedByAsync(factory, () => client.GetAsync(ItemEndpoint(Guid.NewGuid())));

        Assert.Empty(records);
    }

    [Fact]
    public async Task MissingItem_Returns404()
    {
        var (client, _) = await CreateUserAsync(factory, "missing_item_404");

        var response = await client.GetAsync(ItemEndpoint(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_Returns401AndLeavesNoRecord()
    {
        var (owner, _) = await CreateUserAsync(factory, "dl_anonymous");
        var itemId = await UploadAsync(owner);
        HttpResponseMessage? response = null;

        var records = await RecordsAddedByAsync(factory, async () => response = await factory.CreateClient().GetAsync(ItemEndpoint(itemId)));

        Assert.Equal(HttpStatusCode.Unauthorized, response!.StatusCode);
        Assert.Empty(records);
    }

    // --- AC6: the lists are not audited ---

    [Fact]
    public async Task ListDataItems_LeavesNoRecord()
    {
        var scenario = await ArrangeClassifiedAsync(factory, "list_items");

        var records = await RecordsAddedByAsync(factory, () => scenario.Owner.GetAsync(DataItemsEndpoint));

        Assert.Empty(records);
    }

    [Fact]
    public async Task ListClassificationOptions_LeavesNoRecord()
    {
        var scenario = await ArrangeClassifiedAsync(factory, "list_options");

        var records = await RecordsAddedByAsync(factory, () => scenario.Owner.GetAsync(ClassificationOptionsEndpoint));

        Assert.Empty(records);
    }

    private sealed record DownloadResponse(Guid Id, string? Content);
}
