using NetArchTest.Rules;
using Xunit;
using static DataTrustHub.Features.Tests.Architecture.ArchitectureConstants;

namespace DataTrustHub.Features.Tests.Architecture;

public class FeatureIsolationTests
{
    private static readonly IReadOnlyList<string> FeatureNames = DiscoverFeatureNames();

    [Fact]
    public void FeatureDiscovery_FindsTheKnownFeatures()
    {
        Assert.Contains("Authentication", FeatureNames);
        Assert.Contains("DataManagement", FeatureNames);
        Assert.DoesNotContain(SharedFolder, FeatureNames);
    }

    [Fact]
    public void Features_ShouldNotDependOnOtherFeatures()
    {
        var failingTypeNames = FeatureNames
            .SelectMany(FindCrossFeatureViolations)
            .ToList();

        ArchitectureAssert.Passed(failingTypeNames, "A feature must not import another feature");
    }

    [Fact]
    public void Shared_ShouldNotDependOnAnyFeature()
    {
        var result = Types.InAssembly(FeaturesAssembly)
            .That().ResideInNamespaceMatching(NamespaceOrChildren(SharedNamespace))
            .ShouldNot().HaveDependencyOnAny(FeatureNames.Select(ToNamespace).ToArray())
            .GetResult();

        ArchitectureAssert.Passed(result, "_Shared must not depend on a feature");
    }

    private static IEnumerable<string> FindCrossFeatureViolations(string feature)
    {
        var otherFeatures = FeatureNames
            .Where(name => name != feature)
            .Select(ToNamespace)
            .ToArray();

        var result = Types.InAssembly(FeaturesAssembly)
            .That().ResideInNamespaceMatching(NamespaceOrChildren(ToNamespace(feature)))
            .ShouldNot().HaveDependencyOnAny(otherFeatures)
            .GetResult();

        return result.FailingTypeNames ?? [];
    }

    private static string ToNamespace(string feature) => FeaturesRoot + NamespaceSeparator + feature;

    // A feature is the first namespace segment below DataTrustHub.Features, except _Shared
    private static IReadOnlyList<string> DiscoverFeatureNames()
    {
        var featuresPrefix = FeaturesRoot + NamespaceSeparator;

        return FeaturesAssembly.GetTypes()
            .Select(type => type.Namespace)
            .OfType<string>()
            .Where(ns => ns.StartsWith(featuresPrefix, StringComparison.Ordinal))
            .Select(ns => ns[featuresPrefix.Length..].Split(NamespaceSeparator)[0])
            .Where(name => name != SharedFolder)
            .Distinct()
            .ToList();
    }
}
