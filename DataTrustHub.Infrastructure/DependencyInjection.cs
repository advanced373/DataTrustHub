using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Auditing;
using DataTrustHub.Domain.Clearances;
using DataTrustHub.Domain.DataItems;
using DataTrustHub.Domain.Memberships;
using DataTrustHub.Domain.Organizations;
using DataTrustHub.Domain.Policies;
using DataTrustHub.Domain.Users;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Auditing;
using DataTrustHub.Infrastructure.Persistance.Repositories;
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
            // The audit log builds its own context from these same options, so it retries too (ADR-0016)
            services.AddDbContext<DContext>(options =>
            {
                options.UseSqlServer(
                    configuration.GetConnectionString("DefaultConnection"),
                    sqlServer => sqlServer.EnableRetryOnFailure(
                        SqlServerRetryPolicy.MaxRetryCount,
                        SqlServerRetryPolicy.MaxRetryDelay,
                        errorNumbersToAdd: null));
            });

            services.AddSingleton<IPasswordHasher, PasswordHasher>();
            AddRepositories(services);
            AddAuditLog(services);

            return services;
        }

        // Scoped like DContext, so the repositories of one request share it with the unit of work (ADR-0012)
        private static void AddRepositories(IServiceCollection services)
        {
            services.AddScoped<IUserRepository, UserRepository>();
            services.AddScoped<IOrganizationRepository, OrganizationRepository>();
            services.AddScoped<IPolicyRepository, PolicyRepository>();
            services.AddScoped<IClearanceRepository, ClearanceRepository>();
            services.AddScoped<IDataItemRepository, DataItemRepository>();
            services.AddScoped<IMembershipRepository, MembershipRepository>();
            services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<DContext>());
        }

        // Scoped like the context options it builds its own short-lived context from; it never shares the
        // request's DContext, so an audit record is saved apart from the command's unit of work (ADR-0016)
        private static void AddAuditLog(IServiceCollection services) =>
            services.AddScoped<IAuditLog, AuditLog>();
    }
}
