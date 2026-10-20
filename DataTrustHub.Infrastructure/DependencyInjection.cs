using DataTrustHub.Domain.User;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DataTrustHub.Infrastructure
{
    public static class InfrastructureDependencyInjection
    {
        /// <summary>Registers the database context and infrastructure services.</summary>
        /// <param name="services">Service collection.</param>
        /// <returns>An <see cref="IServiceCollection"/>.</returns>
        public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddDbContext<DContext>(options =>
            {
                options.UseSqlServer(configuration.GetConnectionString("DefaultConnection"));
            });

            services.AddSingleton<IPasswordHasher, PasswordHasher>();

            return services;
        }
    }
}
