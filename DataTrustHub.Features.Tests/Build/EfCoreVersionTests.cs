using System.Reflection;
using System.Text.Json;
using DataTrustHub.Features.Tests.Build.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace DataTrustHub.Features.Tests.Build;

// AC4 and AC5 of #140: the pinned dotnet-ef tool, the ProductVersion annotation of every migration and of the
// model snapshot, and the EF Core the tests run on are all the EF Core version the projects reference
public class EfCoreVersionTests
{
    private const string EfCorePackage = "Microsoft.EntityFrameworkCore";
    private const string ToolsProperty = "tools";
    private const string DotnetEfTool = "dotnet-ef";
    private const string VersionProperty = "version";
    private const string RollForwardProperty = "rollForward";
    private const char BuildMetadataSeparator = '+';

    private static readonly Assembly InfrastructureAssembly = typeof(DContext).Assembly;

    public static TheoryData<string> Migrations() =>
    [
        .. InfrastructureAssembly.GetTypes()
            .Where(type => type.IsSubclassOf(typeof(Migration)) && !type.IsAbstract)
            .Select(type => type.FullName!)
    ];

    [Fact]
    public void DotnetEfTool_IsPinnedToTheEfCoreVersion()
    {
        Assert.Equal(EfCoreVersion(), DotnetEfToolEntry().GetProperty(VersionProperty).GetString());
    }

    [Fact]
    public void DotnetEfTool_DoesNotRollForward()
    {
        Assert.False(DotnetEfToolEntry().GetProperty(RollForwardProperty).GetBoolean());
    }

    [Fact]
    public void Migrations_AreFound()
    {
        Assert.NotEmpty(Migrations());
    }

    [Theory]
    [MemberData(nameof(Migrations))]
    public void Migration_ProductVersion_IsTheEfCoreVersion(string migrationType)
    {
        var migration = (Migration)Activator.CreateInstance(InfrastructureAssembly.GetType(migrationType)!)!;

        Assert.Equal(EfCoreVersion(), migration.TargetModel!.GetProductVersion());
    }

    [Fact]
    public void ModelSnapshot_ProductVersion_IsTheEfCoreVersion()
    {
        var snapshotType = InfrastructureAssembly.GetTypes().Single(type => type.IsSubclassOf(typeof(ModelSnapshot)));
        var snapshot = (ModelSnapshot)Activator.CreateInstance(snapshotType)!;

        Assert.Equal(EfCoreVersion(), snapshot.Model.GetProductVersion());
    }

    [Fact]
    public void EfCoreAtRuntime_IsTheReferencedVersion()
    {
        var informational = typeof(DbContext).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

        Assert.Equal(EfCoreVersion(), informational.Split(BuildMetadataSeparator)[0]);
    }

    private static string EfCoreVersion() =>
        SolutionFiles.PackageReferences().Where(reference => reference.Name == EfCorePackage)
            .Select(reference => reference.Version).Distinct().Single();

    private static JsonElement DotnetEfToolEntry()
    {
        using var manifest = JsonDocument.Parse(SolutionFiles.ReadText(SolutionFiles.ToolManifestFile));
        return manifest.RootElement.GetProperty(ToolsProperty).GetProperty(DotnetEfTool).Clone();
    }
}
