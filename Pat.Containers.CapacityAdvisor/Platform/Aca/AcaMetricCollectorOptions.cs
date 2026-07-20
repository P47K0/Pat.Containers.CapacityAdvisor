namespace Pat.Containers.CapacityAdvisor.Platform.Aca;

public sealed class AcaMetricCollectorOptions
{
    public const string SectionName = "PlatformCollectors:Aca";

    public string SubscriptionId { get; init; } = default!;
    public string ResourceGroup { get; init; } = default!;
    public string ContainerAppName { get; init; } = default!;
    public string EnvironmentName { get; init; } = default!;
    public string? ContainerName { get; init; }
    public int MetricWindowMinutes { get; init; } = 10;
}