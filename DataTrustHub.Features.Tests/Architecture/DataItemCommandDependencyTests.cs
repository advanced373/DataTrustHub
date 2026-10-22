using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Clearances;
using DataTrustHub.Domain.DataItems;
using DataTrustHub.Domain.Users;
using DataTrustHub.Features.DataManagement.AddDataItem;
using DataTrustHub.Features.DataManagement.DeleteDataItem;
using NetArchTest.Rules;
using Xunit;
using static DataTrustHub.Features.Tests.Architecture.ArchitectureConstants;

namespace DataTrustHub.Features.Tests.Architecture;

// ADR-0012 step 3a: the DataManagement command handlers work on domain entities through repositories.
// Scoped to these two handlers; the other slices move off DContext in later steps.
public class DataItemCommandDependencyTests
{
    private static readonly Type[] CommandHandlers = [typeof(AddDataItemHandler), typeof(DeleteDataItemHandler)];

    [Fact]
    public void DataItemCommandHandlers_ShouldNotDependOnInfrastructureOrEntityFramework()
    {
        var selection = Types.InAssembly(FeaturesAssembly)
            .That().HaveName(nameof(AddDataItemHandler))
            .Or().HaveName(nameof(DeleteDataItemHandler));
        ArchitectureAssert.NotEmpty(selection, "the DataManagement command handlers");

        var result = selection
            .ShouldNot().HaveDependencyOnAny(InfrastructureNamespace, EntityFrameworkNamespace)
            .GetResult();

        ArchitectureAssert.Passed(result, "DataManagement command handlers must not depend on Infrastructure or EF Core");
    }

    [Fact]
    public void AddDataItemHandler_DependsOnlyOnRepositoriesAndUnitOfWork()
    {
        Type[] expected = [typeof(IUserRepository), typeof(IClearanceRepository), typeof(IDataItemRepository), typeof(IUnitOfWork)];

        Assert.Equal(expected.ToHashSet(), ConstructorDependencies(typeof(AddDataItemHandler)));
    }

    [Fact]
    public void DeleteDataItemHandler_DependsOnlyOnRepositoryAndUnitOfWork()
    {
        Type[] expected = [typeof(IDataItemRepository), typeof(IUnitOfWork)];

        Assert.Equal(expected.ToHashSet(), ConstructorDependencies(typeof(DeleteDataItemHandler)));
    }

    [Fact]
    public void DataItemCommandHandlers_HaveASingleConstructor()
    {
        Assert.All(CommandHandlers, handler => Assert.Single(handler.GetConstructors()));
    }

    private static HashSet<Type> ConstructorDependencies(Type handler) =>
        handler.GetConstructors().Single().GetParameters().Select(p => p.ParameterType).ToHashSet();
}
