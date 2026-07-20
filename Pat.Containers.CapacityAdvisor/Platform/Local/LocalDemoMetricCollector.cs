using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pat.Containers.CapacityAdvisor.Contracts;
using Pat.Containers.CapacityAdvisor.Models;

namespace Pat.Containers.CapacityAdvisor.Platform.Local;

public sealed class LocalDemoMetricCollector : IPlatformMetricCollector
{
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<LocalDemoMetricCollector> _logger;

    public LocalDemoMetricCollector(
        TimeProvider timeProvider,
        ILogger<LocalDemoMetricCollector> logger)
    {
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<MetricCollectionResult> CollectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await Task.CompletedTask;

            var snapshot = new PlatformSnapshot
            {
                Platform = "ACA",
                CollectedAtUtc = _timeProvider.GetUtcNow(),
                EnvironmentName = "local",
                WorkloadName = "demo-app",
                CurrentReplicas = 1,
                CpuUsageCores = 0.15,
                CpuRequestCores = 0.25,
                CpuLimitCores = 0.50,
                MemoryUsageMb = 220,
                MemoryRequestMb = 256,
                MemoryLimitMb = 512
            };

            return MetricCollectionResult.Ok(snapshot);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to collect local metrics.");

            return MetricCollectionResult.Failed("Failed to collect local metrics.");
        }
    }
}