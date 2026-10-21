using System.Reflection;
using NetArchTest.Rules;
using Xunit;
using static DataTrustHub.Features.Tests.Architecture.ArchitectureConstants;

namespace DataTrustHub.Features.Tests.Architecture;

// MediatR and AutoMapper are commercial since 2025; ADR-0012 replaced them with our own handler
// interfaces and Mapperly. These rules keep them from coming back.
public class LicensedDependencyTests
{
    public static TheoryData<string> SolutionAssemblies() =>
    [
        FeaturesAssembly.GetName().Name!,
        SharedKernelAssembly.GetName().Name!,
        DomainAssembly.GetName().Name!,
        InfrastructureAssembly.GetName().Name!
    ];

    [Theory]
    [MemberData(nameof(SolutionAssemblies))]
    public void Assembly_ShouldNotDependOnCommercialLibraries(string assemblyName)
    {
        var result = Types.InAssembly(Assembly.Load(assemblyName))
            .ShouldNot().HaveDependencyOnAny(MediatRNamespace, AutoMapperNamespace)
            .GetResult();

        ArchitectureAssert.Passed(result, $"{assemblyName} must not depend on MediatR or AutoMapper");
    }
}
