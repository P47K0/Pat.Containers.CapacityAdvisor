using Pat.Containers.CapacityAdvisor.Models;
using Pat.Containers.CapacityAdvisor.Models.Webhook;
using Pat.Containers.CapacityAdvisor.Storage;
using System.Text.Json;

namespace Pat.Containers.CapacityAdvisor.Services;

public sealed class AzureMonitorAlertService : IAzureMonitorAlertService
{
    private readonly IAlertHistoryRepository _historyRepository;
    private readonly ICapacityStatusRepository _statusRepository;
    private readonly ICapacityAdvisorService _advisor;
    private readonly ILogger<AzureMonitorAlertService> _logger;

    public AzureMonitorAlertService(
        IAlertHistoryRepository historyRepository,
        ICapacityStatusRepository statusRepository,
        ICapacityAdvisorService advisor,
        ILogger<AzureMonitorAlertService> logger)
    {
        _historyRepository = historyRepository;
        _statusRepository = statusRepository;
        _advisor = advisor;
        _logger = logger;
    }

    public async Task HandleAsync(AzureMonitorCommonAlert payload, CancellationToken cancellationToken)
    {
        var essentials = payload?.Data?.Essentials;
        if (essentials is null)
        {
            return;
        }

        var clusterName = TryGetClusterName(essentials, payload.Data!.AlertContext);
        var @namespace = TryGetNamespace(payload.Data.AlertContext);
        var workloadName = TryGetWorkloadName(payload.Data.AlertContext);
        var signalType = DetectSignalType(essentials, payload.Data.AlertContext);

        var entity = AlertHistoryMapper.MapFromWebhook(
            clusterName,
            @namespace,
            workloadName,
            signalType,
            payload);

        var exists = await _historyRepository.ExistsAsync(
            entity.PartitionKey,
            entity.ExternalAlertId,
            entity.MonitorCondition,
            entity.FiredAtUtc,
            cancellationToken);

        if (exists)
        {
            _logger.LogInformation("Duplicate alert ignored: {AlertId}", entity.ExternalAlertId);
            return;
        }

        await _historyRepository.AddAsync(entity, cancellationToken);

        if (!string.Equals(entity.MonitorCondition, "Fired", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var recentAlerts = await _historyRepository.GetRecentAsync(entity.PartitionKey, 3, entity.SignalType, cancellationToken);
        var trend = BuildTrend(recentAlerts);

        var assessment = await _advisor.AssessFromAlertAsync(
            entity.ClusterName,
            entity.Namespace,
            entity.WorkloadName,
            entity.SignalType,
            trend,
            cancellationToken);

        _logger.LogInformation(
        "Advice computed for {Cluster}/{Namespace}/{Workload}/{Signal}:{Summary}",
        entity.ClusterName,
        entity.Namespace,
        entity.WorkloadName,
        entity.SignalType,
        assessment.Recommendation);

        await _statusRepository.UpsertAsync(
            CapacityStatusMapper.Map(
                entity,
                trend),
            cancellationToken);
    }

    private static AlertTrendSummary BuildTrend(IReadOnlyList<AlertHistoryEntity> alerts)
    {
        var ordered = alerts.OrderBy(a => a.FiredAtUtc).ToList();
        var first = ordered.FirstOrDefault()?.ObservedValue;
        var last = ordered.LastOrDefault()?.ObservedValue;

        var worsening = ordered.Count >= 3 &&
                        ordered.All(a => a.ObservedValue.HasValue) &&
                        ordered[0].ObservedValue <= ordered[1].ObservedValue &&
                        ordered[1].ObservedValue <= ordered[2].ObservedValue;

        return new AlertTrendSummary
        {
            AlertCount = ordered.Count,
            IsRecurring = ordered.Count >= 2,
            IsWorsening = worsening,
            FirstValue = first,
            LastValue = last,
            Summary = worsening
                ? "Repeated alert with increasing observed values."
                : ordered.Count >= 2
                    ? "Repeated alert without a clear worsening trend."
                    : "Single alert; may be transient."
        };
    }

    private static string DetectSignalType(AzureMonitorAlertEssentials essentials, JsonElement context)
    {
        var rule = essentials.AlertRule ?? string.Empty;

        if (rule.Contains("throttl", StringComparison.OrdinalIgnoreCase))
        {
            return "CpuThrottling";
        }

        if (rule.Contains("memory", StringComparison.OrdinalIgnoreCase))
        {
            return "HighMemoryUsage";
        }

        return "Unknown";
    }

    private static string TryGetClusterName(AzureMonitorAlertEssentials essentials, JsonElement context)
    {
        if (essentials.AlertTargetIDs is { Length: > 0 })
        {
            var target = essentials.AlertTargetIDs[0];
            var marker = "/managedClusters/";
            var index = target.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                return target[(index + marker.Length)..];
            }
        }

        return string.Empty;
    }

    private static string TryGetNamespace(JsonElement context)
        => TryFindDimensionValue(context, "namespace") ?? string.Empty;

    private static string TryGetWorkloadName(JsonElement context)
        => TryFindDimensionValue(context, "deployment") ??
           TryFindDimensionValue(context, "workload") ??
           TryFindDimensionValue(context, "pod") ??
           string.Empty;

    private static string? TryFindDimensionValue(JsonElement context, string name)
    {
        if (context.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (context.TryGetProperty("condition", out var condition) &&
            condition.ValueKind == JsonValueKind.Object &&
            condition.TryGetProperty("allOf", out var allOf) &&
            allOf.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in allOf.EnumerateArray())
            {
                if (!item.TryGetProperty("dimensions", out var dimensions) ||
                    dimensions.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var dimension in dimensions.EnumerateArray())
                {
                    if (!dimension.TryGetProperty("name", out var dimName) ||
                        !dimension.TryGetProperty("value", out var dimValue))
                    {
                        continue;
                    }

                    if (string.Equals(dimName.GetString(), name, StringComparison.OrdinalIgnoreCase))
                    {
                        return dimValue.GetString();
                    }
                }
            }
        }

        return null;
    }
}