using Carter;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Features._Shared.Jwt;
using DataTrustHub.Features._Shared.Seeding;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace DataTrustHub.Features._Shared.Extensions;

public static class FeaturesRegistration
{
    public static IServiceCollection AddFeatures(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddCarter();
        RegisterHandlers(services);
        RegisterJwt(services, configuration);
        RegisterAdminSeeding(services, configuration);
        return services;
    }

    public static IEndpointRouteBuilder MapFeatures(this IEndpointRouteBuilder app)
    {
        app.MapCarter();
        return app;
    }

    // Scrutor: Scan registers every slice handler, Decorate wraps each one in the validation decorator.
    // Further cross-cutting decorators (e.g. auditing, #24) are added with another Decorate call;
    // the last one registered is the outermost (ADR-0012).
    private static void RegisterHandlers(IServiceCollection services)
    {
        services.Scan(scan => scan
            .FromAssemblies(typeof(FeaturesRegistration).Assembly)
            .AddClasses(classes => classes.AssignableTo(typeof(ICommandHandler<,>)), publicOnly: false)
                .AsImplementedInterfaces()
                .WithScopedLifetime()
            .AddClasses(classes => classes.AssignableTo(typeof(IQueryHandler<,>)), publicOnly: false)
                .AsImplementedInterfaces()
                .WithScopedLifetime());

        services.Decorate(typeof(ICommandHandler<,>), typeof(CommandValidationDecorator<,>));
        services.Decorate(typeof(IQueryHandler<,>), typeof(QueryValidationDecorator<,>));

        services.AddValidatorsFromAssembly(
            typeof(FeaturesRegistration).Assembly,
            includeInternalTypes: true);
    }

    private static void RegisterJwt(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtConfig>(configuration.GetSection(JwtConfig.SectionName));
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();

        var jwtConfig = configuration.GetSection(JwtConfig.SectionName).Get<JwtConfig>()
            ?? throw new InvalidOperationException("Jwt configuration section is missing.");

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = BuildTokenValidationParameters(jwtConfig);
            });
    }

    private static void RegisterAdminSeeding(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AdminSeedOptions>(configuration.GetSection(AdminSeedOptions.SectionName));
        services.AddHostedService<AdminSeeder>();
    }

    private static TokenValidationParameters BuildTokenValidationParameters(JwtConfig config) =>
        new()
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = config.Issuer,
            ValidAudience = config.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(config.Secret))
        };
}
