using Pat.Containers.CapacityAdvisor.Agents.Cloudflare;
using Pat.Containers.CapacityAdvisor.Contracts;
using Pat.Containers.CapacityAdvisor.Enums;
using Pat.Containers.CapacityAdvisor.Models;

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

    public async Task<CapacityAssessment> AssessAsync(CancellationToken cancellationToken = default)
    {
        var result = await _collector.CollectAsync(cancellationToken);

        if (!result.Success || result.Snapshot is null)
        {
            return new CapacityAssessment
            {
                Success = false,
                ErrorMessage = result.ErrorMessage,
                Recommendation = CapacityRecommendation.Unknown("Metric collection failed.")
            };
        }

        var snapshot = result.Snapshot;

        var cpuUsagePercent = snapshot.CpuLimitCores > 0
            ? (snapshot.CpuUsageCores / snapshot.CpuLimitCores) * 100d
            : 0d;

        var memoryUsagePercent = snapshot.MemoryLimitMb > 0
            ? (snapshot.MemoryUsageMb / snapshot.MemoryLimitMb) * 100d
            : 0d;

        var recommendation = BuildRecommendation(snapshot, cpuUsagePercent, memoryUsagePercent);

        LlmAdviceResponse? llmAdvice = null;

        try
        {
            var llmRequest = LlmAdviceRequestMapper.Map(
                snapshot,
                recommendation.Status,
                recommendation.Reason);

            llmAdvice = await _adviceExplanationService.GenerateAdviceAsync(
                llmRequest,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to generate LLM advice. Returning deterministic assessment only.");
        }

        return new CapacityAssessment
        {
            Success = true,
            Snapshot = snapshot,
            CpuUsagePercent = Math.Round(cpuUsagePercent, 2),
            MemoryUsagePercent = Math.Round(memoryUsagePercent, 2),
            Recommendation = recommendation,
            LlmAdvice = llmAdvice
        };
    }

    private static CapacityRecommendation BuildRecommendation(
        PlatformSnapshot snapshot,
        double cpuUsagePercent,
        double memoryUsagePercent)
    {
        if (snapshot is AksPlatformSnapshot aksSnapshot)
        {
            return BuildAksRecommendation(aksSnapshot, cpuUsagePercent, memoryUsagePercent);
        }

        return BuildGenericRecommendation(snapshot, cpuUsagePercent, memoryUsagePercent);
    }

    private static CapacityRecommendation BuildAksRecommendation(
        AksPlatformSnapshot snapshot,
        double cpuUsagePercent,
        double memoryUsagePercent)
    {
        if (snapshot.Placement.Mode == AksAdviceMode.LimitOnly)
        {
            var cpuIncrease = snapshot.Placement.ShouldIncreaseCpuLimit;
            var memoryIncrease = snapshot.Placement.ShouldIncreaseMemoryLimit;

            if (cpuIncrease || memoryIncrease)
            {
                return new CapacityRecommendation
                {
                    Status = "LimitIncreaseRecommended",
                    Summary = BuildLimitOnlySummary(cpuIncrease, memoryIncrease),
                    Reason = snapshot.Placement.Reason,
                    SuggestedCpuLimitCores = cpuIncrease
                        ? SuggestCpuIncrease(snapshot.CpuLimitCores)
                        : snapshot.CpuLimitCores,
                    SuggestedMemoryLimitMb = memoryIncrease
                        ? SuggestMemoryIncrease(snapshot.MemoryLimitMb)
                        : snapshot.MemoryLimitMb
                };
            }

            return new CapacityRecommendation
            {
                Status = "LimitOnlyTelemetry",
                Summary = "Managed Prometheus is unavailable and no immediate limit pressure was detected.",
                Reason = snapshot.Placement.Reason,
                SuggestedCpuLimitCores = snapshot.CpuLimitCores,
                SuggestedMemoryLimitMb = snapshot.MemoryLimitMb
            };
        }

        if (snapshot.Placement.NeedsNewNode)
        {
            return new CapacityRecommendation
            {
                Status = "NeedsNewNode",
                Summary = "The workload requests do not fit on any current AKS node.",
                Reason = snapshot.Placement.Reason,
                SuggestedCpuLimitCores = snapshot.CpuLimitCores,
                SuggestedMemoryLimitMb = snapshot.MemoryLimitMb
            };
        }

        if (snapshot.Placement.FitsExistingNode)
        {
            if (snapshot.Placement.ShouldIncreaseCpuLimit || snapshot.Placement.ShouldIncreaseMemoryLimit)
            {
                return new CapacityRecommendation
                {
                    Status = "FitsButScaleUpLimits",
                    Summary = "The workload fits on an existing AKS node, but limit pressure is high.",
                    Reason = BuildAksFitReason(snapshot, cpuUsagePercent, memoryUsagePercent),
                    SuggestedCpuLimitCores = snapshot.Placement.ShouldIncreaseCpuLimit
                        ? SuggestCpuIncrease(snapshot.CpuLimitCores)
                        : snapshot.CpuLimitCores,
                    SuggestedMemoryLimitMb = snapshot.Placement.ShouldIncreaseMemoryLimit
                        ? SuggestMemoryIncrease(snapshot.MemoryLimitMb)
                        : snapshot.MemoryLimitMb
                };
            }

            if (string.Equals(snapshot.Placement.RiskLevel, "High", StringComparison.OrdinalIgnoreCase))
            {
                return new CapacityRecommendation
                {
                    Status = "FitsHighRisk",
                    Summary = "The workload fits on an existing AKS node, but remaining headroom is tight.",
                    Reason = BuildAksFitReason(snapshot, cpuUsagePercent, memoryUsagePercent),
                    SuggestedCpuLimitCores = snapshot.CpuLimitCores,
                    SuggestedMemoryLimitMb = snapshot.MemoryLimitMb
                };
            }

            if (string.Equals(snapshot.Placement.RiskLevel, "Medium", StringComparison.OrdinalIgnoreCase))
            {
                return new CapacityRecommendation
                {
                    Status = "FitsWatch",
                    Summary = "The workload fits on an existing AKS node, but should be watched closely.",
                    Reason = BuildAksFitReason(snapshot, cpuUsagePercent, memoryUsagePercent),
                    SuggestedCpuLimitCores = snapshot.CpuLimitCores,
                    SuggestedMemoryLimitMb = snapshot.MemoryLimitMb
                };
            }

            return new CapacityRecommendation
            {
                Status = "FitsExistingNode",
                Summary = "The workload fits on an existing AKS node with acceptable headroom.",
                Reason = BuildAksFitReason(snapshot, cpuUsagePercent, memoryUsagePercent),
                SuggestedCpuLimitCores = snapshot.CpuLimitCores,
                SuggestedMemoryLimitMb = snapshot.MemoryLimitMb
            };
        }

        return new CapacityRecommendation
        {
            Status = "AksReviewRequired",
            Summary = "AKS telemetry was collected, but the result could not be classified confidently.",
            Reason = snapshot.Placement.Reason,
            SuggestedCpuLimitCores = snapshot.CpuLimitCores,
            SuggestedMemoryLimitMb = snapshot.MemoryLimitMb
        };
    }

    private static CapacityRecommendation BuildGenericRecommendation(
        PlatformSnapshot snapshot,
        double cpuUsagePercent,
        double memoryUsagePercent)
    {
        if (memoryUsagePercent >= 85 || cpuUsagePercent >= 85)
        {
            return new CapacityRecommendation
            {
                Status = "ScaleUp",
                Summary = "Workload is approaching its configured limit.",
                Reason = $"CPU {cpuUsagePercent:F1}% and memory {memoryUsagePercent:F1}% of limit.",
                SuggestedCpuLimitCores = SuggestCpuIncrease(snapshot.CpuLimitCores),
                SuggestedMemoryLimitMb = SuggestMemoryIncrease(snapshot.MemoryLimitMb)
            };
        }

        if (memoryUsagePercent >= 65 || cpuUsagePercent >= 65)
        {
            return new CapacityRecommendation
            {
                Status = "Watch",
                Summary = "Workload is healthy but trending toward higher utilization.",
                Reason = $"CPU {cpuUsagePercent:F1}% and memory {memoryUsagePercent:F1}% of limit.",
                SuggestedCpuLimitCores = snapshot.CpuLimitCores,
                SuggestedMemoryLimitMb = snapshot.MemoryLimitMb
            };
        }

        return new CapacityRecommendation
        {
            Status = "Healthy",
            Summary = "Workload has comfortable remaining headroom.",
            Reason = $"CPU {cpuUsagePercent:F1}% and memory {memoryUsagePercent:F1}% of limit.",
            SuggestedCpuLimitCores = snapshot.CpuLimitCores,
            SuggestedMemoryLimitMb = snapshot.MemoryLimitMb
        };
    }

    private static string BuildLimitOnlySummary(bool cpuIncrease, bool memoryIncrease)
    {
        if (cpuIncrease && memoryIncrease)
        {
            return "Managed Prometheus is unavailable. CPU and memory limits should both be increased.";
        }

        if (cpuIncrease)
        {
            return "Managed Prometheus is unavailable. CPU limit should be increased.";
        }

        return "Managed Prometheus is unavailable. Memory limit should be increased.";
    }

    private static string BuildAksFitReason(
        AksPlatformSnapshot snapshot,
        double cpuUsagePercent,
        double memoryUsagePercent)
    {
        var nodeText = string.IsNullOrWhiteSpace(snapshot.Placement.RecommendedNode)
            ? "No specific node recommendation was returned."
            : $"Recommended node: {snapshot.Placement.RecommendedNode}.";

        return $"{snapshot.Placement.Reason} {nodeText} CPU {cpuUsagePercent:F1}% and memory {memoryUsagePercent:F1}% of limit.";
    }

    private static double SuggestCpuIncrease(double currentCpuLimitCores)
    {
        if (currentCpuLimitCores <= 0)
        {
            return 0;
        }

        return currentCpuLimitCores < 1
            ? currentCpuLimitCores + 0.25
            : currentCpuLimitCores + 0.50;
    }

    private static double SuggestMemoryIncrease(double currentMemoryLimitMb)
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