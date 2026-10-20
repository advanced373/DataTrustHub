using Carter;
using FluentValidation;
using MediatR;
using NetArchTest.Rules;
using Xunit;
using static DataTrustHub.Features.Tests.Architecture.ArchitectureConstants;

namespace DataTrustHub.Features.Tests.Architecture;

public class NamingConventionTests
{
    [Fact]
    public void RequestHandlers_ShouldEndWithHandler()
    {
        var handlers = SliceClasses().And().ImplementInterface(typeof(IRequestHandler<,>));

        ArchitectureAssert.NotEmpty(handlers, nameof(IRequestHandler<IRequest<object>, object>));
        ArchitectureAssert.Passed(
            handlers.Should().HaveNameEndingWith(HandlerSuffix).GetResult(),
            $"Request handlers must end with '{HandlerSuffix}'");
    }

    [Fact]
    public void Validators_ShouldEndWithValidator()
    {
        var validators = SliceClasses().And().Inherit(typeof(AbstractValidator<>));

        ArchitectureAssert.NotEmpty(validators, nameof(AbstractValidator<object>));
        ArchitectureAssert.Passed(
            validators.Should().HaveNameEndingWith(ValidatorSuffix).GetResult(),
            $"Validators must end with '{ValidatorSuffix}'");
    }

    [Fact]
    public void CarterModules_ShouldEndWithEndpoint()
    {
        var endpoints = SliceClasses().And().ImplementInterface(typeof(ICarterModule));

        ArchitectureAssert.NotEmpty(endpoints, nameof(ICarterModule));
        ArchitectureAssert.Passed(
            endpoints.Should().HaveNameEndingWith(EndpointSuffix).GetResult(),
            $"Carter modules must end with '{EndpointSuffix}'");
    }

    // _Shared holds infrastructure (e.g. the validation pipeline), so only slices follow the slice naming
    private static PredicateList SliceClasses() =>
        Types.InAssembly(FeaturesAssembly)
            .That().AreClasses()
            .And().DoNotResideInNamespaceMatching(NamespaceOrChildren(SharedNamespace));
}
