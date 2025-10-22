using Microsoft.Extensions.DependencyInjection;

namespace DataTrustHub.Application;

public static class ApplicationDependencyInjection
{
	public static IServiceCollection AddApplication(this IServiceCollection services)
	{
		_ = services.AddMediatR(config =>
		{
			_ = config.RegisterServicesFromAssembly(typeof(ApplicationDependencyInjection).Assembly);
			//_ = config.AddOpenBehavior(typeof(ValidationPipelineBehavior<,>));
		});

		//_ = services.AddValidatorsFromAssembly(typeof(ApplicationDependencyInjection).Assembly, includeInternalTypes: true);

		return services;
	}
}