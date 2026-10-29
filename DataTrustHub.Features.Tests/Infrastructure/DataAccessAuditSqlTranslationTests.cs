using System.Net;
using DataTrustHub.Features.Tests.Infrastructure.Helpers;
using Xunit;
using static DataTrustHub.Features.Tests.Administration.Helpers.MembershipSetup;
using static DataTrustHub.Features.Tests.Auditing.Helpers.DataAccessAuditSetup;

namespace DataTrustHub.Features.Tests.Infrastructure;

// #24 step 2 on a relational provider: the download's left join to the policy (its organization) and the denied
// path's existence/organization subquery must translate to SQL, which EF InMemory does not check
public class DataAccessAuditSqlTranslationTests(SqliteApiFactory factory) : IClassFixture<SqliteApiFactory>
{
    [Fact]
    public async Task Download_OnSqlite_Classified_RecordsSucceededWithThePolicysOrganization()
    {
        var scenario = await ArrangeClassifiedAsync(factory, "sql_dl_classified");
        HttpResponseMessage? response = null;

        var records = await RecordsAddedByAsync(factory, async () => response = await scenario.Owner.GetAsync(ItemEndpoint(scenario.ItemId)));

        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
        AssertRecord(Assert.Single(records), scenario.OwnerId, scenario.ItemId, scenario.OrganizationId, Succeeded, null);
    }

    [Fact]
    public async Task DeniedRead_OnSqlite_ClassifiedWithoutClearance_RecordsDeniedWithThePolicysOrganization()
    {
        var scenario = await ArrangeClassifiedAsync(factory, "sql_deny_classified");
        var (intruder, intruderId) = await CreateUserAsync(factory, "sql_deny_classified_intruder");
        HttpResponseMessage? response = null;

        var records = await RecordsAddedByAsync(factory, async () => response = await intruder.GetAsync(ItemEndpoint(scenario.ItemId)));

        Assert.Equal(HttpStatusCode.NotFound, response!.StatusCode);
        AssertRecord(Assert.Single(records), intruderId, scenario.ItemId, scenario.OrganizationId, Denied, DataItemNotFoundCode);
    }

    [Fact]
    public async Task DeniedRead_OnSqlite_PolicyNoLongerExists_RecordsDeniedWithoutOrganization()
    {
        var scenario = await ArrangeClassifiedAsync(factory, "sql_deny_no_policy");
        await RemovePolicyAsync(factory, scenario.PolicyId);
        var (intruder, intruderId) = await CreateUserAsync(factory, "sql_deny_no_policy_intruder");
        HttpResponseMessage? response = null;

        var records = await RecordsAddedByAsync(factory, async () => response = await intruder.GetAsync(ItemEndpoint(scenario.ItemId)));

        Assert.Equal(HttpStatusCode.NotFound, response!.StatusCode);
        AssertRecord(Assert.Single(records), intruderId, scenario.ItemId, null, Denied, DataItemNotFoundCode);
    }

    [Fact]
    public async Task MissingItem_OnSqlite_LeavesNoRecordAndReturns404()
    {
        var (client, _) = await CreateUserAsync(factory, "sql_missing_item");
        HttpResponseMessage? response = null;

        var records = await RecordsAddedByAsync(factory, async () => response = await client.GetAsync(ItemEndpoint(Guid.NewGuid())));

        Assert.Equal(HttpStatusCode.NotFound, response!.StatusCode);
        Assert.Empty(records);
    }
}
