using System.Net;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.Features.Tests.Infrastructure.Helpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DataTrustHub.Features.Tests.Authentication.Helpers;

/// <summary>
/// The real host on SQLite for register (#172): the purge of expired registration requests is a set-based
/// delete that EF InMemory cannot run. The clock is a <see cref="TestClock"/> the test moves; a request may
/// name its client address in <see cref="ClientAddressHeader"/> (TestServer gives none), so the per-address
/// Registration rate limit can be tested. Unless a test sets it, the rate limit is high enough not to interfere.
/// </summary>
public class RegistrationApiFactory : SqliteApiFactory
{
    internal const string ClientAddressHeader = "X-Test-Client-Address";

    internal const string LifetimeDaysSetting = "Registration:PendingRequestLifetimeDays";
    internal const string PermitsPerHourPerIpSetting = "Registration:PermitsPerHourPerIp";
    internal const string MaxPendingRequestsSetting = "Registration:MaxPendingRequests";

    // Far above what a test class sends, so only the rate-limit tests meet the limit
    internal const string UnlimitedPermits = "100000";

    private readonly IDictionary<string, string?> _settings;

    public RegistrationApiFactory() : this(new Dictionary<string, string?>())
    {
    }

    // Settings the test gives win; without PermitsPerHourPerIp the limit is lifted, with null it is the
    // configured one (appsettings.json)
    internal RegistrationApiFactory(IDictionary<string, string?> settings)
    {
        _settings = new Dictionary<string, string?>(settings);
        _settings.TryAdd(PermitsPerHourPerIpSetting, UnlimitedPermits);
    }

    public TestClock Clock { get; } = new();

    // Builds and starts a host for one test (the class fixture is started by xUnit)
    internal static async Task<RegistrationApiFactory> StartAsync(IDictionary<string, string?> settings)
    {
        var factory = new RegistrationApiFactory(settings);
        await StartFactoryAsync(factory);
        return factory;
    }

    // Starts the host; a host that fails to start is disposed and the failure is rethrown
    internal static async Task StartFactoryAsync(RegistrationApiFactory factory)
    {
        try
        {
            await ((IAsyncLifetime)factory).InitializeAsync();
        }
        catch
        {
            await factory.StopAsync();
            throw;
        }
    }

    internal Task StopAsync() => ((IAsyncLifetime)this).DisposeAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        var given = _settings.Where(s => s.Value is not null).ToDictionary(s => s.Key, s => s.Value);
        OverrideSettings(builder, given);

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton<IStartupFilter, ClientAddressStartupFilter>();
        });
    }

    // Sets the connection's remote address from the test header, before the rate limiter reads it
    private sealed class ClientAddressStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue(ClientAddressHeader, out var address))
                    context.Connection.RemoteIpAddress = IPAddress.Parse(address.ToString());
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
