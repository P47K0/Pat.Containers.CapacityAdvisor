using Microsoft.Extensions.Options;

namespace Pat.Containers.CapacityAdvisor.Platform.Aks;

public sealed class AksMetricCollectorOptionsValidator : IValidateOptions<AksMetricCollectorOptions>
{
    public ValidateOptionsResult Validate(string? name, AksMetricCollectorOptions options)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.SubscriptionId))
        {
            errors.Add($"{AksMetricCollectorOptions.SectionName}:SubscriptionId is required.");
        }

        if (string.IsNullOrWhiteSpace(options.ResourceGroup))
        {
            errors.Add($"{AksMetricCollectorOptions.SectionName}:ResourceGroup is required.");
        }

        if (string.IsNullOrWhiteSpace(options.ClusterName))
        {
            errors.Add($"{AksMetricCollectorOptions.SectionName}:ClusterName is required.");
        }

        if (string.IsNullOrWhiteSpace(options.Namespace))
        {
            errors.Add($"{AksMetricCollectorOptions.SectionName}:Namespace is required.");
        }

        if (string.IsNullOrWhiteSpace(options.WorkloadName))
        {
            errors.Add($"{AksMetricCollectorOptions.SectionName}:WorkloadName is required.");
        }

        if (string.IsNullOrWhiteSpace(options.WorkloadKind))
        {
            errors.Add($"{AksMetricCollectorOptions.SectionName}:WorkloadKind is required.");
        }

        if (options.MetricWindowMinutes <= 0)
        {
            errors.Add($"{AksMetricCollectorOptions.SectionName}:MetricWindowMinutes must be greater than 0.");
        }

        if (options.MetricWindowMinutes > 1440)
        {
            errors.Add($"{AksMetricCollectorOptions.SectionName}:MetricWindowMinutes must be 1440 or less.");
        }

        if (!string.IsNullOrWhiteSpace(options.WorkloadKind) &&
            !IsSupportedWorkloadKind(options.WorkloadKind))
        {
            errors.Add(
                $"{AksMetricCollectorOptions.SectionName}:WorkloadKind must be one of: Deployment, StatefulSet, DaemonSet, ReplicaSet, Pod.");
        }

        return errors.Count > 0
            ? ValidateOptionsResult.Fail(errors)
            : ValidateOptionsResult.Success;
    }

    private static bool IsSupportedWorkloadKind(string value) =>
         value.Equals("Deployment", StringComparison.OrdinalIgnoreCase) ||
         value.Equals("StatefulSet", StringComparison.OrdinalIgnoreCase) ||
         value.Equals("DaemonSet", StringComparison.OrdinalIgnoreCase) ||
         value.Equals("ReplicaSet", StringComparison.OrdinalIgnoreCase) ||
         value.Equals("Pod", StringComparison.OrdinalIgnoreCase);
}