using Microsoft.Extensions.Options;

namespace Pat.Containers.CapacityAdvisor.Platform.Aca;

public sealed class AcaMetricCollectorOptionsValidator : IValidateOptions<AcaMetricCollectorOptions>
{
    public ValidateOptionsResult Validate(string? name, AcaMetricCollectorOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.SubscriptionId))
            failures.Add("PlatformCollectors:Aca:SubscriptionId is required.");

        if (string.IsNullOrWhiteSpace(options.ResourceGroup))
            failures.Add("PlatformCollectors:Aca:ResourceGroup is required.");

        if (string.IsNullOrWhiteSpace(options.ContainerAppName))
            failures.Add("PlatformCollectors:Aca:ContainerAppName is required.");

        if (string.IsNullOrWhiteSpace(options.EnvironmentName))
            failures.Add("PlatformCollectors:Aca:EnvironmentName is required.");

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}