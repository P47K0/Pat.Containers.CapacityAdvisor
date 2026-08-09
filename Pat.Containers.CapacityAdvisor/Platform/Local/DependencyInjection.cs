using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pat.Containers.CapacityAdvisor.Contracts;
using Pat.Containers.CapacityAdvisor.Platform.Aks;

namespace Pat.Containers.CapacityAdvisor.Platform.Local;

public static class LocalServiceCollectionExtensions
{
    public static IServiceCollection AddLocalMetricCollector(
        this IServiceCollection services,
        IConfiguration configuration)
    {                
        services.AddScoped<IPlatformMetricCollector, LocalDemoMetricCollector>();

        return services;
    }
}