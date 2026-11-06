using System.Reflection;
using System.Runtime.CompilerServices;
using DataTrustHub.Domain.Auditing;
using DataTrustHub.Domain.ReadModels.ClassificationOptions;
using DataTrustHub.Domain.ReadModels.DataItems;
using DataTrustHub.Features.DataManagement.GetDataItem;
using DataTrustHub.Features.DataManagement.ListClassificationOptions;
using DataTrustHub.Features.DataManagement.ListDataItems;
using DataTrustHub.Features.Tests.Build.Helpers;
using DataTrustHub.Infrastructure.Persistance.ReadModels;
using Microsoft.Extensions.Logging;
using Xunit;
using static DataTrustHub.Features.Tests.Architecture.ArchitectureConstants;

namespace DataTrustHub.Features.Tests.Architecture;

// #74 (ADR-0012 step 4, part 4a): the DataManagement query side reads through the read-side ports: the read rule
// (DataAccess) moved out of Features into Infrastructure, and the exact constructor dependencies of the three query
// handlers. #74 step 5 (AC4): the read rule's SQL form is internal, and only the test project sees the
// Infrastructure internals, for the equivalence tests. The layering rule (FeaturesPersistenceIsolationTests) and the
// ports and read models (ReadSideArchitectureTests) are checked project-wide
public class DataManagementReadSideArchitectureTests
{
    private const string DataManagementFolder = "DataManagement";
    private const string DataManagementNamespace = FeaturesRoot + NamespaceSeparator + DataManagementFolder;
    private const string FeaturesProjectFolder = "DataTrustHub.Features";
    private const string OldReadRuleFile = "DataAccess.cs";
    private const string OldReadRuleType = DataManagementNamespace + NamespaceSeparator + "DataAccess";
    private const string ReadRuleTypeName = "DataAccess";
    private const string TestsAssemblyName = "DataTrustHub.Features.Tests";

    [Fact]
    public void ReadRule_IsNoLongerPartOfFeatures()
    {
        Assert.Null(FeaturesAssembly.GetType(OldReadRuleType));
        Assert.DoesNotContain(FeaturesAssembly.GetTypes(), type => type.Name == ReadRuleTypeName);
        Assert.False(File.Exists(Path.Combine(DataManagementRoot(), OldReadRuleFile)));
    }

    [Fact]
    public void ReadRule_LivesInInfrastructure()
    {
        Assert.Contains(InfrastructureAssembly.GetTypes(), type => type.Name == ReadRuleTypeName && type.IsAbstract && type.IsSealed);
    }

    // AC4: no Features handler can call the SQL form of the read and delete rule
    [Fact]
    public void ReadRule_IsInternal()
    {
        Assert.Same(InfrastructureAssembly, typeof(DataAccess).Assembly);
        Assert.True(typeof(DataAccess).IsNotPublic, $"{nameof(DataAccess)} must be internal");
    }

    // AC4: the test project, for the equivalence tests (#99), is the only assembly that sees the Infrastructure internals
    [Fact]
    public void InfrastructureInternals_AreVisibleOnlyToTheTestProject()
    {
        var friends = InfrastructureAssembly.GetCustomAttributes<InternalsVisibleToAttribute>()
            .Select(attribute => attribute.AssemblyName)
            .ToList();

        Assert.Equal([TestsAssemblyName], friends);
        Assert.Equal(TestsAssemblyName, Assembly.GetExecutingAssembly().GetName().Name);
    }

    [Fact]
    public void ListDataItemsHandler_DependsOnlyOnTheDataItemQueries()
    {
        Assert.Equal(new HashSet<Type> { typeof(IDataItemQueries) }, ConstructorDependencies(typeof(ListDataItemsHandler)));
    }

    [Fact]
    public void GetDataItemHandler_DependsOnlyOnTheDataItemQueriesTheAuditLogAndALogger()
    {
        Type[] expected = [typeof(IDataItemQueries), typeof(IAuditLog), typeof(ILogger<GetDataItemHandler>)];

        Assert.Equal(expected.ToHashSet(), ConstructorDependencies(typeof(GetDataItemHandler)));
    }

    [Fact]
    public void ListClassificationOptionsHandler_DependsOnlyOnTheClassificationOptionQueries()
    {
        Assert.Equal(
            new HashSet<Type> { typeof(IClassificationOptionQueries) },
            ConstructorDependencies(typeof(ListClassificationOptionsHandler)));
    }

    private static HashSet<Type> ConstructorDependencies(Type handler) =>
        handler.GetConstructors().Single().GetParameters().Select(p => p.ParameterType).ToHashSet();

    private static string DataManagementRoot() =>
        Path.Combine(SolutionFiles.RepositoryRoot, FeaturesProjectFolder, DataManagementFolder);
}
