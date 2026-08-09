using Pat.Containers.CapacityAdvisor.Agents.Cloudflare;
using Pat.Containers.CapacityAdvisor.Contracts;
using Pat.Containers.CapacityAdvisor.Enums;
using Pat.Containers.CapacityAdvisor.Models;
using Pat.Containers.CapacityAdvisor.Models.Webhook;
using Pat.Containers.CapacityAdvisor.Storage;

namespace Pat.Containers.CapacityAdvisor.Services;

public sealed class CapacityAdvisorService : ICapacityAdvisorService
{
    private readonly IPlatformMetricCollector _collector;
    private readonly IAdviceExplanationService _adviceExplanationService;
    private readonly ILogger<CapacityAdvisorService> _logger;

    public CapacityAdvisorService(
        IPlatformMetricCollector collector,
        IAdviceExplanationService adviceExplanationService,
        ILogger<CapacityAdvisorService> logger)
    {
        _collector = collector;
        _adviceExplanationService = adviceExplanationService;
        _logger = logger;
    }

    public async Task<CapacityAssessment> AssessAsync(
        CancellationToken cancellationToken = default)
    {
        var result =
            await _collector.CollectAsync(cancellationToken);

        if (!result.Success || result.Snapshot is null)
        {
            return FailedAssessment(
                result.ErrorMessage ??
                "Metric collection failed.");
        }

        var snapshot = result.Snapshot;

        var percentages =
            CalculateUsagePercentages(snapshot);

        var recommendation =
            BuildRecommendation(
                snapshot,
                percentages.CpuUsagePercent,
                percentages.MemoryUsagePercent);

        var llmAdvice =
            await TryGenerateAdviceAsync(
                snapshot,
                recommendation,
                cancellationToken);

        return CreateAssessment(
            snapshot,
            percentages.CpuUsagePercent,
            percentages.MemoryUsagePercent,
            recommendation,
            llmAdvice);
    }

    public Task AssessAsync(
        string clusterName,
        string @namespace,
        string workloadName,
        string signalType,
        AlertTrendSummary trend,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Assessing capacity for cluster {ClusterName}, namespace {Namespace}, workload {WorkloadName}, signal {SignalType}. Trend: {Trend}",
            clusterName,
            @namespace,
            workloadName,
            signalType,
            trend.Summary);

        return Task.CompletedTask;
    }

    public async Task<CapacityAssessment> AssessFromAlertAsync(
        string clusterName,
        string @namespace,
        string workloadName,
        string signalType,
        AlertTrendSummary trend,
        CancellationToken cancellationToken)
    {
        var result =
            await _collector.CollectAsync(cancellationToken);

        if (!result.Success || result.Snapshot is null)
        {
            return FailedAssessment(
                result.ErrorMessage ??
                "Unable to collect a fresh capacity snapshot.",
                trend.Summary);
        }

        var snapshot = result.Snapshot;

        var percentages =
            CalculateUsagePercentages(snapshot);

        var recommendation =
            BuildRecommendation(
                snapshot,
                trend,
                percentages.CpuUsagePercent,
                percentages.MemoryUsagePercent);

        var llmAdvice =
            await TryGenerateAdviceAsync(
                snapshot,
                recommendation,
                cancellationToken);

        return CreateAssessment(
            snapshot,
            percentages.CpuUsagePercent,
            percentages.MemoryUsagePercent,
            recommendation,
            llmAdvice);
    }

    public async Task<CapacityAssessment> GetRecommendationAsync(
        CapacityStatusEntity storedStatus,
        IReadOnlyList<AlertHistoryEntity> recentAlerts,
        CancellationToken cancellationToken)
    {
        var result =
            await _collector.CollectAsync(cancellationToken);

        if (!result.Success || result.Snapshot is null)
        {
            return FailedAssessment(
                result.ErrorMessage ??
                "Unable to collect current platform metrics.");
        }

        var snapshot = result.Snapshot;

        var percentages =
            CalculateUsagePercentages(snapshot);

        var recommendation =
            BuildRecommendation(
                snapshot,
                storedStatus,
                recentAlerts,
                percentages.CpuUsagePercent,
                percentages.MemoryUsagePercent);

        // Do not add another historical summary here.
        // BuildRecommendation already adds it once.
        var llmAdvice =
            await TryGenerateAdviceAsync(
                snapshot,
                recommendation,
                cancellationToken,
                storedStatus,
                recentAlerts);

        return CreateAssessment(
            snapshot,
            percentages.CpuUsagePercent,
            percentages.MemoryUsagePercent,
            recommendation,
            llmAdvice);
    }

    private static CapacityRecommendation BuildRecommendation(
        PlatformSnapshot snapshot,
        double cpuUsagePercent,
        double memoryUsagePercent)
    {
        return snapshot is AksPlatformSnapshot aksSnapshot
            ? BuildAksRecommendation(
                aksSnapshot,
                trendIsWorsening: false,
                cpuUsagePercent,
                memoryUsagePercent)
            : BuildGenericRecommendation(
                snapshot,
                trendIsWorsening: false,
                cpuUsagePercent,
                memoryUsagePercent);
    }

    private static CapacityRecommendation BuildRecommendation(
        PlatformSnapshot snapshot,
        AlertTrendSummary trend,
        double cpuUsagePercent,
        double memoryUsagePercent)
    {
        var recommendation =
            snapshot is AksPlatformSnapshot aksSnapshot
                ? BuildAksRecommendation(
                    aksSnapshot,
                    trend.IsWorsening,
                    cpuUsagePercent,
                    memoryUsagePercent)
                : BuildGenericRecommendation(
                    snapshot,
                    trend.IsWorsening,
                    cpuUsagePercent,
                    memoryUsagePercent);

        return AddHistoricalTrendContext(
            recommendation,
            trend);
    }

    private static CapacityRecommendation BuildRecommendation(
        PlatformSnapshot snapshot,
        CapacityStatusEntity storedStatus,
        IReadOnlyList<AlertHistoryEntity> recentAlerts,
        double cpuUsagePercent,
        double memoryUsagePercent)
    {
        var trendIsWorsening =
            IsWorsening(storedStatus.TrendState);

        var recommendation =
            snapshot is AksPlatformSnapshot aksSnapshot
                ? BuildAksRecommendation(
                    aksSnapshot,
                    trendIsWorsening,
                    cpuUsagePercent,
                    memoryUsagePercent)
                : BuildGenericRecommendation(
                    snapshot,
                    trendIsWorsening,
                    cpuUsagePercent,
                    memoryUsagePercent);

        return AddHistoricalAlertContext(
            recommendation,
            storedStatus,
            recentAlerts,
            cpuUsagePercent,
            memoryUsagePercent);
    }

    private static CapacityRecommendation BuildAksRecommendation(
        AksPlatformSnapshot snapshot,
        bool trendIsWorsening,
        double cpuUsagePercent,
        double memoryUsagePercent)
    {
        var placement = snapshot.Placement;

        if (placement.Mode == AksAdviceMode.LimitOnly)
        {
            var cpuIncrease =
                placement.ShouldIncreaseCpuLimit;

            var memoryIncrease =
                placement.ShouldIncreaseMemoryLimit;

            if (cpuIncrease || memoryIncrease)
            {
                return new CapacityRecommendation
                {
                    Status = "LimitIncreaseRecommended",
                    Summary = BuildLimitOnlySummary(
                        cpuIncrease,
                        memoryIncrease),
                    Reason = placement.Reason,
                    SuggestedCpuLimitCores =
                        cpuIncrease
                            ? SuggestCpuIncrease(
                                snapshot.CpuLimitCores)
                            : snapshot.CpuLimitCores,
                    SuggestedMemoryLimitMb =
                        memoryIncrease
                            ? SuggestMemoryIncrease(
                                snapshot.MemoryLimitMb)
                            : snapshot.MemoryLimitMb
                };
            }

            return new CapacityRecommendation
            {
                Status = "LimitOnlyTelemetry",
                Summary =
                    "Managed Prometheus is unavailable and no immediate limit pressure was detected.",
                Reason = placement.Reason,
                SuggestedCpuLimitCores =
                    snapshot.CpuLimitCores,
                SuggestedMemoryLimitMb =
                    snapshot.MemoryLimitMb
            };
        }

        if (placement.NeedsNewNode)
        {
            return CapacityRecommendation.ScaleOut(
                summary:
                    "The workload requests do not fit on any current AKS node.",
                reason:
                    placement.Reason,
                suggestedCpuLimitCores:
                    snapshot.CpuLimitCores,
                suggestedMemoryLimitMb:
                    snapshot.MemoryLimitMb);
        }

        if (placement.FitsExistingNode)
        {
            if (placement.ShouldIncreaseCpuLimit ||
                placement.ShouldIncreaseMemoryLimit)
            {
                return new CapacityRecommendation
                {
                    Status = "FitsButScaleUpLimits",
                    Summary =
                        "The workload fits on an existing AKS node, but limit pressure is high.",
                    Reason =
                        BuildAksFitReason(
                            snapshot,
                            cpuUsagePercent,
                            memoryUsagePercent),
                    SuggestedCpuLimitCores =
                        placement.ShouldIncreaseCpuLimit
                            ? SuggestCpuIncrease(
                                snapshot.CpuLimitCores)
                            : snapshot.CpuLimitCores,
                    SuggestedMemoryLimitMb =
                        placement.ShouldIncreaseMemoryLimit
                            ? SuggestMemoryIncrease(
                                snapshot.MemoryLimitMb)
                            : snapshot.MemoryLimitMb
                };
            }

            if (string.Equals(
                    placement.RiskLevel,
                    "High",
                    StringComparison.OrdinalIgnoreCase))
            {
                return new CapacityRecommendation
                {
                    Status = "FitsHighRisk",
                    Summary =
                        "The workload fits on an existing AKS node, but remaining headroom is tight.",
                    Reason =
                        BuildAksFitReason(
                            snapshot,
                            cpuUsagePercent,
                            memoryUsagePercent),
                    SuggestedCpuLimitCores =
                        snapshot.CpuLimitCores,
                    SuggestedMemoryLimitMb =
                        snapshot.MemoryLimitMb
                };
            }

            if (string.Equals(
                    placement.RiskLevel,
                    "Medium",
                    StringComparison.OrdinalIgnoreCase))
            {
                return new CapacityRecommendation
                {
                    Status = "FitsWatch",
                    Summary =
                        "The workload fits on an existing AKS node, but should be watched closely.",
                    Reason =
                        BuildAksFitReason(
                            snapshot,
                            cpuUsagePercent,
                            memoryUsagePercent),
                    SuggestedCpuLimitCores =
                        snapshot.CpuLimitCores,
                    SuggestedMemoryLimitMb =
                        snapshot.MemoryLimitMb
                };
            }

            return CapacityRecommendation.FitsExistingNode(
                summary:
                    "The workload fits on an existing AKS node with acceptable headroom.",
                reason:
                    BuildAksFitReason(
                        snapshot,
                        cpuUsagePercent,
                        memoryUsagePercent),
                suggestedCpuLimitCores:
                    snapshot.CpuLimitCores,
                suggestedMemoryLimitMb:
                    snapshot.MemoryLimitMb);
        }

        return CapacityRecommendation.Unknown(
            $"AKS telemetry was collected, but the result could not be classified confidently. {placement.Reason}");
    }

    private static CapacityRecommendation BuildGenericRecommendation(
        PlatformSnapshot snapshot,
        bool trendIsWorsening,
        double cpuUsagePercent,
        double memoryUsagePercent)
    {
        var cpuPressure =
            cpuUsagePercent >= 85;

        var memoryPressure =
            memoryUsagePercent >= 85;

        var elevatedUsage =
            cpuUsagePercent >= 65 ||
            memoryUsagePercent >= 65;

        var suggestedCpuLimit =
            cpuPressure
                ? SuggestCpuIncrease(
                    snapshot.CpuLimitCores)
                : snapshot.CpuLimitCores;

        var suggestedMemoryLimit =
            memoryPressure
                ? SuggestMemoryIncrease(
                    snapshot.MemoryLimitMb)
                : snapshot.MemoryLimitMb;

        if (cpuPressure || memoryPressure)
        {
            var status =
                cpuPressure && memoryPressure
                    ? "IncreaseResourceLimits"
                    : cpuPressure
                        ? "IncreaseCpuLimit"
                        : "IncreaseMemoryLimit";

            var summary =
                cpuPressure && memoryPressure
                    ? "CPU and memory usage are approaching their configured limits."
                    : cpuPressure
                        ? "CPU usage is approaching its configured limit."
                        : "Memory usage is approaching its configured limit.";

            var action =
                cpuPressure && memoryPressure
                    ? "Increase the CPU and memory limits, then continue monitoring."
                    : cpuPressure
                        ? "Increase the CPU limit, then continue monitoring."
                        : "Increase the memory limit, then continue monitoring.";

            return new CapacityRecommendation
            {
                Status = status,
                Summary = summary,
                Reason =
                    $"Current usage is elevated. " +
                    $"Current CPU usage is {cpuUsagePercent:F1}% and " +
                    $"memory usage is {memoryUsagePercent:F1}%.",
                RecommendedAction = action,
                SuggestedCpuLimitCores = suggestedCpuLimit,
                SuggestedMemoryLimitMb = suggestedMemoryLimit
            };
        }

        if (elevatedUsage)
        {
            return CapacityRecommendation.FitsExistingNode(
                summary:
                    "The workload is below its limits but should be monitored.",
                reason:
                    $"Current usage is elevated. " +
                    $"Current CPU usage is {cpuUsagePercent:F1}% and " +
                    $"memory usage is {memoryUsagePercent:F1}%.",
                suggestedCpuLimitCores:
                    snapshot.CpuLimitCores,
                suggestedMemoryLimitMb:
                    snapshot.MemoryLimitMb);
        }

        return CapacityRecommendation.FitsExistingNode(
            summary:
                trendIsWorsening
                    ? "The workload is currently operating normally after previous pressure alerts."
                    : "The workload is running normally with comfortable headroom.",
            reason:
                $"Current usage is normal. " +
                $"Current CPU usage is {cpuUsagePercent:F1}% and " +
                $"memory usage is {memoryUsagePercent:F1}%.",
            suggestedCpuLimitCores:
                snapshot.CpuLimitCores,
            suggestedMemoryLimitMb:
                snapshot.MemoryLimitMb);
    }

    private static CapacityRecommendation AddHistoricalAlertContext(
    CapacityRecommendation recommendation,
    CapacityStatusEntity storedStatus,
    IReadOnlyList<AlertHistoryEntity> recentAlerts,
    double cpuUsagePercent,
    double memoryUsagePercent)
    {
        var alertContext =
            BuildAlertHistoryContext(
                storedStatus,
                recentAlerts);

        var currentUsageIsNormal =
            cpuUsagePercent < 65 &&
            memoryUsagePercent < 65;

        var isNonActionStatus =
            recommendation.Status == "FitsExistingNode" ||
            recommendation.Status == "Healthy" ||
            recommendation.Status == "FitsWatch";

        var alertCount =
            recentAlerts.Count > 0
                ? recentAlerts.Count
                : storedStatus.AlertCount;

        var summary =
            recommendation.Summary;

        if (currentUsageIsNormal &&
            IsWorsening(storedStatus.TrendState) &&
            isNonActionStatus)
        {
            summary =
                $"The workload is currently operating normally after " +
                $"{alertCount} historical pressure alert(s).";
        }

        return new CapacityRecommendation
        {
            Status = recommendation.Status,
            Summary = summary,

            Reason =
                $"{alertContext} {recommendation.Reason}".Trim(),

            RecommendedAction =
                recommendation.RecommendedAction,

            SuggestedCpuLimitCores =
                recommendation.SuggestedCpuLimitCores,

            SuggestedMemoryLimitMb =
                recommendation.SuggestedMemoryLimitMb
        };
    }

    private static CapacityRecommendation AddHistoricalTrendContext(
        CapacityRecommendation recommendation,
        AlertTrendSummary trend)
    {
        return new CapacityRecommendation
        {
            Status = recommendation.Status,
            Summary = recommendation.Summary,
            Reason =
                $"{trend.AlertCount} alert(s) were observed. " +
                $"Historical trend: {GetTrendState(trend)}. " +
                $"{trend.Summary} {recommendation.Reason}",
            RecommendedAction =
                recommendation.RecommendedAction,
            SuggestedCpuLimitCores =
                recommendation.SuggestedCpuLimitCores,
            SuggestedMemoryLimitMb =
                recommendation.SuggestedMemoryLimitMb
        };
    }

    private static string BuildAlertHistoryContext(
        CapacityStatusEntity storedStatus,
        IReadOnlyList<AlertHistoryEntity> recentAlerts)
    {
        var alertCount =
            recentAlerts.Count > 0
                ? recentAlerts.Count
                : storedStatus.AlertCount;

        var alertGroups =
            recentAlerts
                .GroupBy(
                    alert => GetAlertCategory(alert),
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key)
                .ToList();

        var categoryText =
            alertGroups.Count == 0
                ? "No alert categories were available."
                : string.Join(
                    ", ",
                    alertGroups.Select(
                        group =>
                            $"{group.Count()} {group.Key} alert(s)"));

        var trendState =
                FormatTrendState(
                    storedStatus.TrendState);

        return
            $"{alertCount} historical alert(s) were recorded " +
            $"for this workload. " +
            $"Alert categories: {categoryText}. " +
            $"The historical trend is {trendState}.";
    }

    private static string GetAlertCategory(
        AlertHistoryEntity alert)
    {
        if (!string.IsNullOrWhiteSpace(alert.RuleName))
        {
            return alert.RuleName;
        }

        if (!string.IsNullOrWhiteSpace(alert.SignalType))
        {
            return alert.SignalType;
        }

        return "Unknown";
    }

    private static string FormatTrendState(
    string? trendState)
    {
        if (string.Equals(
                trendState,
                "Worsening",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                trendState,
                "Increasing",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                trendState,
                "Critical",
                StringComparison.OrdinalIgnoreCase))
        {
            return "worsening";
        }

        if (string.Equals(
                trendState,
                "Recurring",
                StringComparison.OrdinalIgnoreCase))
        {
            return "recurring";
        }

        if (string.Equals(
                trendState,
                "Single",
                StringComparison.OrdinalIgnoreCase))
        {
            return "single occurrence";
        }

        return "unknown";
    }

    private async Task<LlmAdviceResponse?> TryGenerateAdviceAsync(
        PlatformSnapshot snapshot,
        CapacityRecommendation recommendation,
        CancellationToken cancellationToken,
        CapacityStatusEntity? storedStatus = null,
        IReadOnlyList<AlertHistoryEntity>? recentAlerts = null
        )
    {
        try
        {
            var llmRequest =
                LlmAdviceRequestMapper.Map(
                    snapshot,
                    recommendation.Status,
                    recommendation.Reason,
                    recentAlerts: recentAlerts,
                    recommendation: recommendation,
                    storedStatus: storedStatus);

            return await _adviceExplanationService
                .GenerateAdviceAsync(
                    llmRequest,
                    cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to generate LLM advice. Returning deterministic assessment only.");

            return null;
        }
    }

    private static CapacityAssessment CreateAssessment(
        PlatformSnapshot snapshot,
        double cpuUsagePercent,
        double memoryUsagePercent,
        CapacityRecommendation recommendation,
        LlmAdviceResponse? llmAdvice)
    {
        return new CapacityAssessment
        {
            Success = true,
            Snapshot = snapshot,
            CpuUsagePercent =
                Math.Round(cpuUsagePercent, 2),
            MemoryUsagePercent =
                Math.Round(memoryUsagePercent, 2),
            Recommendation = recommendation,
            LlmAdvice = llmAdvice
        };
    }

    private static CapacityAssessment FailedAssessment(
        string errorMessage,
        string? reason = null)
    {
        return new CapacityAssessment
        {
            Success = false,
            ErrorMessage = errorMessage,
            Recommendation =
                CapacityRecommendation.Unknown(
                    reason ?? errorMessage)
        };
    }

    private static (
        double CpuUsagePercent,
        double MemoryUsagePercent)
        CalculateUsagePercentages(
            PlatformSnapshot snapshot)
    {
        var cpuUsagePercent =
            snapshot.CpuLimitCores > 0
                ? snapshot.CpuUsageCores /
                  snapshot.CpuLimitCores *
                  100d
                : 0d;

        var memoryUsagePercent =
            snapshot.MemoryLimitMb > 0
                ? snapshot.MemoryUsageMb /
                  snapshot.MemoryLimitMb *
                  100d
                : 0d;

        return (
            cpuUsagePercent,
            memoryUsagePercent);
    }

    private static bool IsWorsening(
        string? trendState)
    {
        return string.Equals(
                   trendState,
                   "Increasing",
                   StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                   trendState,
                   "Worsening",
                   StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                   trendState,
                   "Critical",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string GetTrendState(
        AlertTrendSummary trend)
    {
        if (trend.IsWorsening)
        {
            return "worsening";
        }

        if (trend.IsRecurring)
        {
            return "recurring";
        }

        return "single";
    }

    private static string BuildLimitOnlySummary(
        bool cpuIncrease,
        bool memoryIncrease)
    {
        if (cpuIncrease && memoryIncrease)
        {
            return
                "Managed Prometheus is unavailable. CPU and memory limits should both be increased.";
        }

        if (cpuIncrease)
        {
            return
                "Managed Prometheus is unavailable. CPU limit should be increased.";
        }

        return
            "Managed Prometheus is unavailable. Memory limit should be increased.";
    }

    private static string BuildAksFitReason(
        AksPlatformSnapshot snapshot,
        double cpuUsagePercent,
        double memoryUsagePercent)
    {
        var nodeText =
            string.IsNullOrWhiteSpace(
                snapshot.Placement.RecommendedNode)
                    ? "No specific node recommendation was returned."
                    : $"Recommended node: {snapshot.Placement.RecommendedNode}.";

        return
            $"{snapshot.Placement.Reason} {nodeText} " +
            $"CPU is {cpuUsagePercent:F1}% and " +
            $"memory is {memoryUsagePercent:F1}% of the configured limits.";
    }

    private static double SuggestCpuIncrease(
        double currentCpuLimitCores)
    {
        if (currentCpuLimitCores <= 0)
        {
            return 0;
        }

        return currentCpuLimitCores < 1
            ? currentCpuLimitCores + 0.25
            : currentCpuLimitCores + 0.50;
    }

    private static double SuggestMemoryIncrease(
        double currentMemoryLimitMb)
    {
        if (currentMemoryLimitMb <= 0)
        {
            return 0;
        }

        return currentMemoryLimitMb < 1024
            ? currentMemoryLimitMb + 256
            : currentMemoryLimitMb + 512;
    }
}