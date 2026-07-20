namespace Pat.Containers.CapacityAdvisor.Contracts;

public interface IPlatformMetricCollector
{
    Task<MetricCollectionResult> CollectAsync(CancellationToken cancellationToken = default);
}