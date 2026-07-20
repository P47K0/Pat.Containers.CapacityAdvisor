using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pat.Containers.CapacityAdvisor.Contracts;
using Pat.Containers.CapacityAdvisor.Platform.Aca;

namespace Pat.Containers.CapacityAdvisor.Platform.Aca;

public static class DependencyInjection
{
    public static IServiceCollection AddAcaMetricCollector(
        this IServiceCollection services,
        IConfiguration configuration)
    {
            services.AddOptions<AcaMetricCollectorOptions>()
            .Bind(configuration.GetSection(AcaMetricCollectorOptions.SectionName))
            .Validate(o =>
                !string.IsNullOrWhiteSpace(o.SubscriptionId) &&
                !string.IsNullOrWhiteSpace(o.ResourceGroup) &&
                !string.IsNullOrWhiteSpace(o.ContainerAppName),
                "AcaMetrics configuration is incomplete.")
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<AcaMetricCollectorOptions>, AcaMetricCollectorOptionsValidator>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IPlatformMetricCollector, AcaPlatformMetricCollector>();

        return services;
    }
}