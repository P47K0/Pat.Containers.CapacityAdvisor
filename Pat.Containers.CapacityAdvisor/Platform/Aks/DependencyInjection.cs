using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pat.Containers.CapacityAdvisor.Contracts;

namespace Pat.Containers.CapacityAdvisor.Platform.Aks;

public static class AksServiceCollectionExtensions
{
    public static IServiceCollection AddAksMetricCollector(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<AksMetricCollectorOptions>()
            .Bind(configuration.GetSection(AksMetricCollectorOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<AksMetricCollectorOptions>, AksMetricCollectorOptionsValidator>();

        services.AddScoped<IPlatformMetricCollector, AksPlatformMetricCollector>();

        return services;
    }
}