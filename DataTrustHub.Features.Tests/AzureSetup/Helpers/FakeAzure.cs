namespace DataTrustHub.Features.Tests.AzureSetup.Helpers;

/// <summary>An app setting as the fake Azure holds it.</summary>
internal sealed record FakeSetting(string Name, string? Value);

/// <summary>An App Service plan as the fake Azure holds it.</summary>
internal sealed record FakePlan(string Id, string? Tier);

/// <summary>A web app as the fake Azure holds it.</summary>
internal sealed class FakeApp
{
    public string? IdentityType { get; set; }
    public string? DefaultHostName { get; set; }
    public string? PlanId { get; set; }
    public string? FtpsState { get; set; }
    public string? HealthCheckPath { get; set; }

    /// <summary>The basic publishing credentials policies, scm and ftp: "true" while they are allowed.</summary>
    public Dictionary<string, string> BasicAuth { get; set; } = [];
    public List<FakeSetting> Settings { get; set; } = [];
}

/// <summary>
/// The Azure state the stateful fake az (<see cref="SetupScriptRun"/>) reads and changes. It is written to a
/// JSON file before the run and read back after it, so a second run sees what the first one changed.
/// </summary>
internal sealed class FakeAzure
{
    internal const string ApiAppName = "datatrusthubapi";
    internal const string WebAppName = "DataTrustHub";
    internal const string FreeTier = "Free";
    internal const string BasicTier = "Basic";
    internal const string ProductionPlanId = "/subscriptions/fake/resourceGroups/DataTrustHub/providers/Microsoft.Web/serverfarms/DataTrustHubPlan";
    internal const string OtherPlanId = "/subscriptions/fake/resourceGroups/DataTrustHub/providers/Microsoft.Web/serverfarms/OtherPlan";
    internal const string ApiHostName = "fake-api.example.test";
    internal const string ApiBaseUrl = "https://" + ApiHostName;
    internal const string ExistingJwtSecret = "existing-jwt-secret-value";
    internal const string HealthCheckPath = "/health";
    internal const string FtpsDisabled = "Disabled";
    internal const string FirewallRuleName = "AllowAllWindowsAzureIps";
    internal const string Allowed = "true";
    internal const string NotAllowed = "false";
    internal const string ScmPolicy = "scm";
    internal const string FtpPolicy = "ftp";
    internal const string SystemAssigned = "SystemAssigned";

    // The connection string the API must hold for the production defaults (managed identity, no password)
    internal const string ConnectionString = "Server=tcp:datatrusthub.database.windows.net,1433;Database=datatrusthub;" +
        "Authentication=Active Directory Managed Identity;Encrypt=True;Connect Timeout=60";

    public bool SignedIn { get; set; } = true;
    public string Subscription { get; set; } = "Fake Subscription";
    public Dictionary<string, FakeApp> Apps { get; set; } = [];
    public List<FakePlan> Plans { get; set; } = [];
    public List<string> FirewallRules { get; set; } = [];

    /// <summary>Regular expressions: an az call line matching one exits with an error (a failed read).</summary>
    public List<string> FailingReads { get; set; } = [];

    /// <summary>Kinds of change (<see cref="FakeChange"/>) that exit 0 but change nothing.</summary>
    public List<string> IgnoredChanges { get; set; } = [];

    /// <summary>Every app setting the script sent to az, with its value, as the fake received it.</summary>
    public List<FakeSetting> ReceivedSettings { get; set; } = [];

    /// <summary>Both apps on one plan of the given tier, with nothing of the setup done yet.</summary>
    internal static FakeAzure NothingConfigured(string tier)
    {
        var azure = new FakeAzure { Plans = [new FakePlan(ProductionPlanId, tier)] };
        azure.Apps[ApiAppName] = NewApp(ProductionPlanId, ApiHostName);
        azure.Apps[WebAppName] = NewApp(ProductionPlanId, "fake-webapp.example.test");
        return azure;
    }

    /// <summary>Both apps on one plan of the given tier, with the whole setup already done.</summary>
    internal static FakeAzure FullyConfigured(string tier)
    {
        var azure = NothingConfigured(tier);
        azure.FirewallRules.Add(FirewallRuleName);
        var api = azure.Apps[ApiAppName];
        api.IdentityType = SystemAssigned;
        api.Settings.Add(new FakeSetting(DryRunScenario.ConnectionStringSetting, ConnectionString));
        api.Settings.Add(new FakeSetting(DryRunScenario.JwtSecretSetting, ExistingJwtSecret));
        azure.Apps[WebAppName].Settings.Add(new FakeSetting(SetupScriptRun.ApiBaseUrlSetting, ApiBaseUrl));
        foreach (var app in azure.Apps.Values)
        {
            app.FtpsState = FtpsDisabled;
            app.BasicAuth[ScmPolicy] = NotAllowed;
            app.BasicAuth[FtpPolicy] = NotAllowed;
            app.HealthCheckPath = tier == FreeTier ? null : HealthCheckPath;
        }
        return azure;
    }

    /// <summary>The value of a setting the script sent to az, or null if it sent none.</summary>
    internal string? Received(string settingName) =>
        ReceivedSettings.LastOrDefault(setting => setting.Name == settingName)?.Value;

    private static FakeApp NewApp(string planId, string hostName)
    {
        var app = new FakeApp { PlanId = planId, DefaultHostName = hostName, FtpsState = "AllAllowed" };
        app.BasicAuth[ScmPolicy] = Allowed;
        app.BasicAuth[FtpPolicy] = Allowed;
        foreach (var name in DryRunScenario.ApplicationInsightsSettings)
            app.Settings.Add(new FakeSetting(name, "portal-value"));
        return app;
    }
}

/// <summary>The kinds of change the fake az can be told to ignore (exit 0, change nothing).</summary>
internal static class FakeChange
{
    internal const string Identity = "identity";
    internal const string AppSettings = "appsettings";

    // The setting names are stored, but every value is replaced by a wrong one
    internal const string AppSettingValues = "appsetting-values";
    internal const string Ftps = "ftps";
    internal const string HealthCheck = "healthcheck";
    internal const string BasicAuth = "basicauth";
    internal const string Firewall = "firewall";
}

/// <summary>Patterns of az reads, for <see cref="FakeAzure.FailingReads"/>.</summary>
internal static class FakeRead
{
    internal const string PlanId = @"^webapp show .*appServicePlanId";
    internal const string PlanTier = @"^appservice plan show .*sku\.tier";
}
