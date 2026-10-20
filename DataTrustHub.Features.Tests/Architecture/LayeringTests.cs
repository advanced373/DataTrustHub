using NetArchTest.Rules;
using Xunit;
using static DataTrustHub.Features.Tests.Architecture.ArchitectureConstants;

namespace DataTrustHub.Features.Tests.Architecture;

public class LayeringTests
{
    [Fact]
    public void SharedKernel_ShouldNotDependOnAnySolutionProject()
    {
        var result = Types.InAssembly(SharedKernelAssembly)
            .ShouldNot().HaveDependencyOnAny(
                DomainNamespace, InfrastructureNamespace, ApplicationNamespace, FeaturesRoot, ApiNamespace)
            .GetResult();

        ArchitectureAssert.Passed(result, "SharedKernel must stay dependency-free");
    }

    [Fact]
    public void Domain_ShouldNotDependOnOuterLayers()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot().HaveDependencyOnAny(
                InfrastructureNamespace, ApplicationNamespace, FeaturesRoot, ApiNamespace)
            .GetResult();

        ArchitectureAssert.Passed(result, "Domain must not depend on outer layers");
    }

    [Fact]
    public void Infrastructure_ShouldNotDependOnFeaturesOrApi()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .ShouldNot().HaveDependencyOnAny(FeaturesRoot, ApiNamespace)
            .GetResult();

        ArchitectureAssert.Passed(result, "Infrastructure must not depend on Features or API");
    }

    [Fact]
    public void Features_ShouldNotDependOnApiOrLegacyApplicationLayer()
    {
        var result = Types.InAssembly(FeaturesAssembly)
            .ShouldNot().HaveDependencyOnAny(ApiNamespace, ApplicationNamespace)
            .GetResult();

        ArchitectureAssert.Passed(result, "Features must not depend on the API host or the legacy Application layer");
    }
}
