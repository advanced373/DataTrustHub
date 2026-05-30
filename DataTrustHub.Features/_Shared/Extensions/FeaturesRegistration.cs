using Carter;
using DataTrustHub.Features._Shared.Behaviors;
using DataTrustHub.Features._Shared.Jwt;
using FluentValidation;
using MediatR;
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
        RegisterMediatR(services);
        RegisterJwt(services, configuration);
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
