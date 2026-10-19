using DataTrustHub.Services.Api;
using Microsoft.Extensions.Options;

namespace DataTrustHub.Services;

internal static class ServiceRegistration
{
    internal static IServiceCollection AddWebAppServices(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddTransient<BearerTokenHandler>();
        services.AddApiClients();
        return services;
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
