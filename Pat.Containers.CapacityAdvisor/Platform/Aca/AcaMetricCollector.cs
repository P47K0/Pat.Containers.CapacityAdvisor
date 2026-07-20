using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Monitor.Query;
using Azure.Monitor.Query.Models;
using Azure.ResourceManager;
using Azure.ResourceManager.AppContainers;
using Microsoft.Extensions.Options;
using Pat.Containers.CapacityAdvisor.Contracts;
using Pat.Containers.CapacityAdvisor.Models;
using Pat.Containers.CapacityAdvisor.Options;

namespace Pat.Containers.CapacityAdvisor.Platform.Aca;

public sealed class AcaPlatformMetricCollector : IPlatformMetricCollector
{
    private readonly AcaMetricCollectorOptions _options;
    private readonly TokenCredential _credential;
    private readonly ILogger<AcaPlatformMetricCollector> _logger;

    public AcaPlatformMetricCollector(
        IOptions<AcaMetricCollectorOptions> options,
        ILogger<AcaPlatformMetricCollector> logger)
    {
        _options = options.Value;
        _logger = logger;
        _credential = new DefaultAzureCredential();
    }

    public async Task<MetricCollectionResult> CollectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var armClient = new ArmClient(_credential, _options.SubscriptionId);

            var containerAppResourceId = ContainerAppResource.CreateResourceIdentifier(
                _options.SubscriptionId,
                _options.ResourceGroup,
                _options.ContainerAppName);

            var containerApp = armClient.GetContainerAppResource(containerAppResourceId);
            var containerAppResponse = await containerApp.GetAsync(cancellationToken);
            var data = containerAppResponse.Value.Data;

            var template = data.Template;
            var container = SelectContainer(template?.Containers);

            if (container is null)
            {
                return MetricCollectionResult.Failed(
                    $"No container definition found for ACA app '{_options.ContainerAppName}'.");
            }

            var currentReplicas = await GetCurrentReplicasAsync(containerAppResourceId, cancellationToken);
            var cpuUsageCores = await GetCpuUsageCoresAsync(containerAppResourceId, cancellationToken);
            var memoryUsageMb = await GetMemoryUsageMbAsync(containerAppResourceId, cancellationToken);

            var snapshot = new PlatformSnapshot
            {
                Platform = "ACA",
                WorkloadName = _options.ContainerAppName,
                ResourceId = containerAppResourceId.ToString(),
                CurrentReplicas = currentReplicas,
                CpuRequestCores = container.Resources?.Cpu ?? 0,
                CpuLimitCores = container.Resources?.Cpu ?? 0,
                MemoryRequestMb = ParseMemoryToMb(container.Resources?.Memory),
                MemoryLimitMb = ParseMemoryToMb(container.Resources?.Memory),
                CpuUsageCores = cpuUsageCores,
                MemoryUsageMb = memoryUsageMb,
                CollectedAtUtc = DateTimeOffset.UtcNow
            };

            return MetricCollectionResult.Ok(snapshot);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to collect ACA metrics.");
            return MetricCollectionResult.Failed(ex.Message);
        }
    }

    private Azure.ResourceManager.AppContainers.Models.ContainerAppContainer? SelectContainer(
        IList<Azure.ResourceManager.AppContainers.Models.ContainerAppContainer>? containers)
    {
        if (containers is null || containers.Count == 0)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(_options.ContainerName))
        {
            return containers[0];
        }

        return containers.FirstOrDefault(c =>
            string.Equals(c.Name, _options.ContainerName, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<int> GetCurrentReplicasAsync(
        ResourceIdentifier resourceId,
        CancellationToken cancellationToken)
    {
        var value = await QueryMetricAverageAsync(
            resourceId.ToString(),
            "Replicas",
            cancellationToken);

        return value is null ? 1 : Math.Max(1, (int)Math.Round(value.Value));
    }

    private async Task<double> GetCpuUsageCoresAsync(
        ResourceIdentifier resourceId,
        CancellationToken cancellationToken)
    {
        var milliCores = await QueryMetricAverageAsync(
            resourceId.ToString(),
            "UsageNanoCores",
            cancellationToken);

        if (milliCores is null)
        {
            _logger.LogWarning("CPU metric not found for resource {ResourceId}. Falling back to 0.", resourceId);
            return 0;
        }

        return milliCores.Value / 1000d;
    }

    private async Task<double> GetMemoryUsageMbAsync(
        ResourceIdentifier resourceId,
        CancellationToken cancellationToken)
    {
        var bytes = await QueryMetricAverageAsync(
            resourceId.ToString(),
            "WorkingSetBytes",
            cancellationToken);

        if (bytes is null)
        {
            _logger.LogWarning("Memory metric not found for resource {ResourceId}. Falling back to 0.", resourceId);
            return 0;
        }

        return bytes.Value / 1024d / 1024d;
    }

    private async Task<double?> QueryMetricAverageAsync(
        string resourceId,
        string metricName,
        CancellationToken cancellationToken)
    {
        var client = new MetricsQueryClient(_credential);

        var options = new MetricsQueryOptions
        {
            TimeRange = TimeSpan.FromMinutes(_options.MetricWindowMinutes)
        };

        options.Aggregations.Add(MetricAggregationType.Average);

        Response<MetricsQueryResult> response = await client.QueryResourceAsync(
            resourceId,
            new[] { metricName },
            options,
            cancellationToken);

        var metric = response.Value.Metrics.FirstOrDefault();
        var series = metric?.TimeSeries.FirstOrDefault();
        var point = series?.Values.LastOrDefault(v => v.Average.HasValue);

        return point?.Average;
    }

    private static double ParseMemoryToMb(string? memoryValue)
    {
        if (string.IsNullOrWhiteSpace(memoryValue))
        {
            return 0;
        }

        var value = memoryValue.Trim();

        if (value.EndsWith("Gi", StringComparison.OrdinalIgnoreCase) &&
            double.TryParse(value[..^2], out var gi))
        {
            return gi * 1024;
        }

        if (value.EndsWith("Mi", StringComparison.OrdinalIgnoreCase) &&
            double.TryParse(value[..^2], out var mi))
        {
            return mi;
        }

        if (double.TryParse(value, out var raw))
        {
            return raw;
        }

        return 0;
    }
}