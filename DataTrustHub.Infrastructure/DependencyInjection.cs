
using DataTrustHub.Domain.Clearance;
using DataTrustHub.Domain.Data;
using DataTrustHub.Domain.Organization;
using DataTrustHub.Domain.Policy;
using DataTrustHub.Domain.User;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Repositories.Clearance;
using DataTrustHub.Infrastructure.Persistance.Repositories.Data;
using DataTrustHub.Infrastructure.Persistance.Repositories.Organization;
using DataTrustHub.Infrastructure.Persistance.Repositories.Policy;
using DataTrustHub.Infrastructure.Persistance.Repositories.User;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DataTrustHub.Infrastructure
{
    public static class InfrastructureDependencyInjection
    {
        /// <summary>Application.</summary>
        /// <param name="services">Service collection.</param>
        /// <returns>An <see cref="IServiceCollection"/>.</returns>
        public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddDbContext<DContext>(options =>
            {
                options.UseSqlServer(configuration.GetConnectionString("DefaultConnection"));
            });

            services.AddScoped<IUserRepository, UserRepository>();
            services.AddScoped<IClearanceRepository, ClearanceRepository>();
            services.AddScoped<IOrganizationRepository, OrganizationRepository>();
            services.AddScoped<IPolicyRepository, PolicyRepository>();
            services.AddScoped<IDataItemRepository, DataItemRepository>();

            return services;
        }
    }
}
