using System.Text.RegularExpressions;
using NetArchTest.Rules;
using Xunit;
using static DataTrustHub.Features.Tests.Architecture.ArchitectureConstants;

namespace DataTrustHub.Features.Tests.Architecture;

// ADR-0012: the domain is free of persistence and only changes through its own methods
public class DomainModelTests
{
    private const string EntityKeyProperty = "Id";

    [Fact]
    public void Domain_ShouldNotDependOnEntityFrameworkOrInfrastructure()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot().HaveDependencyOnAny(EntityFrameworkNamespace, InfrastructureNamespace)
            .GetResult();

        ArchitectureAssert.Passed(result, "Domain must not depend on EF Core or Infrastructure");
    }

    [Fact]
    public void Entities_ShouldHaveNoPublicSetters()
    {
        // Read models are flat records for display, not entities (ADR-0012 step 4): their init setters change nothing
        var entities = DomainAssembly.GetTypes()
            .Where(type => type.IsClass && type.GetProperty(EntityKeyProperty) is not null)
            .Where(type => !Regex.IsMatch(type.Namespace ?? string.Empty, NamespaceOrChildren(ReadModelsNamespace)))
            .ToList();

        var publicSetters = entities
            .SelectMany(type => type.GetProperties()
                .Where(property => property.SetMethod?.IsPublic == true)
                .Select(property => $"{type.Name}.{property.Name}"))
            .ToList();

        Assert.NotEmpty(entities);
        ArchitectureAssert.Passed(publicSetters, "Domain entities must not have public setters");
    }
}
