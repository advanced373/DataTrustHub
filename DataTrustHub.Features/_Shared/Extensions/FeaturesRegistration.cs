using Carter;
using DataTrustHub.Features._Shared.Behaviors;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DataTrustHub.Features._Shared.Extensions;

public static class FeaturesRegistration
{
    public static IServiceCollection AddFeatures(
        this IServiceCollection services)
    {
        services.AddCarter();
        RegisterMediatR(services);
        return services;
    }

    public static IEndpointRouteBuilder MapFeatures(this IEndpointRouteBuilder app)
    {
        app.MapCarter();
        return app;
    }

    private static void RegisterMediatR(IServiceCollection services)
    {
        services.AddMediatR(config =>
        {
            config.RegisterServicesFromAssembly(typeof(FeaturesRegistration).Assembly);
            config.AddOpenBehavior(typeof(ValidationPipelineBehavior<,>));
        });

        services.AddValidatorsFromAssembly(
            typeof(FeaturesRegistration).Assembly,
            includeInternalTypes: true);
    }
}
