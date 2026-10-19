using DataTrustHub.Services.Api;
using DataTrustHub.Services.Session;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace DataTrustHub.Services;

internal static class ServiceRegistration
{
    internal static IServiceCollection AddWebAppServices(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<IUserSessionService, UserSessionService>();
        services.AddTransient<BearerTokenHandler>();
        services.AddCookieAuthentication();
        services.AddApiClients();
        return services;
    }

    private static void AddCookieAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.LoginPath = Constants.LoginPath;
                options.LogoutPath = Constants.LogoutPath;
                options.AccessDeniedPath = Constants.AccessDeniedPath;
                options.SlidingExpiration = false;
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Strict;
            });
    }

    private static void AddApiClients(this IServiceCollection services)
    {
        services.AddOptions<ApiSettings>()
            .BindConfiguration(Constants.ApiSectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient<IAuthApiClient, AuthApiClient>(ConfigureBaseAddress);
        services.AddHttpClient<IDataApiClient, DataApiClient>(ConfigureBaseAddress)
            .AddHttpMessageHandler<BearerTokenHandler>();
        services.AddHttpClient<IAdminApiClient, AdminApiClient>(ConfigureBaseAddress)
            .AddHttpMessageHandler<BearerTokenHandler>();
    }

    private static void ConfigureBaseAddress(IServiceProvider provider, HttpClient httpClient)
    {
        var settings = provider.GetRequiredService<IOptions<ApiSettings>>().Value;
        httpClient.BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/') + "/");
    }
}
