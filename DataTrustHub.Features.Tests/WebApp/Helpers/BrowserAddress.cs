using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace DataTrustHub.Features.Tests.WebApp.Helpers;

// TestServer gives a browser request no remote address. A test names the browser's address in Header, and the
// startup filter sets the connection's remote address from it before the WebApp handles the request
internal static class BrowserAddress
{
    internal const string Header = "X-Test-Browser-Address";

    internal static void AddTo(IServiceCollection services) =>
        services.AddSingleton<IStartupFilter, StartupFilter>();

    // A client whose every request comes from the given browser address
    internal static void Set(HttpClient client, string address)
    {
        client.DefaultRequestHeaders.Remove(Header);
        client.DefaultRequestHeaders.Add(Header, address);
    }

    private sealed class StartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue(Header, out var address))
                    context.Connection.RemoteIpAddress = IPAddress.Parse(address.ToString());
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
