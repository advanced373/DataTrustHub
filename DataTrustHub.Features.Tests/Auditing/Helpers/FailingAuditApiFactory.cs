using DataTrustHub.Domain.Auditing;
using DataTrustHub.Features.Tests.Helpers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DataTrustHub.Features.Tests.Auditing.Helpers;

// The API with its audit log replaced by a fake one, so a test can make the audit write fail, like a database
// that is down, after it has arranged its data
public class FailingAuditApiFactory : ApiFactory
{
    internal FakeAuditLog AuditLog { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAuditLog>();
            services.AddSingleton<IAuditLog>(AuditLog);
        });
    }
}
