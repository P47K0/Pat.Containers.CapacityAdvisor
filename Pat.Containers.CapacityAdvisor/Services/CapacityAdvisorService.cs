using Pat.Containers.CapacityAdvisor.Agents.Cloudflare;
using Pat.Containers.CapacityAdvisor.Contracts;
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
            ? (snapshot.CpuUsageCores / snapshot.CpuLimitCores) * 100
            : 0;

        var memoryUsagePercent = snapshot.MemoryLimitMb > 0
            ? (snapshot.MemoryUsageMb / snapshot.MemoryLimitMb) * 100
            : 0;

        var recommendation = BuildRecommendation(snapshot, cpuUsagePercent, memoryUsagePercent);

        LlmAdviceResponse? llmAdvice = null;

        try
        {
            var llmRequest = new LlmAdviceRequest
            {
                Platform = snapshot.Platform,
                WorkloadName = snapshot.WorkloadName,
                CurrentReplicas = snapshot.CurrentReplicas,
                CpuUsagePercent = Math.Round(cpuUsagePercent, 2),
                MemoryUsagePercent = Math.Round(memoryUsagePercent, 2),
                CpuRequestCores = snapshot.CpuRequestCores,
                CpuLimitCores = snapshot.CpuLimitCores,
                MemoryRequestMb = snapshot.MemoryRequestMb,
                MemoryLimitMb = snapshot.MemoryLimitMb,
                DeterministicStatus = recommendation.Status,
                DeterministicReason = recommendation.Reason
            };

            llmAdvice = await _adviceExplanationService.GenerateAdviceAsync(llmRequest, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate LLM advice. Returning deterministic assessment only.");
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
        if (memoryUsagePercent >= 85 || cpuUsagePercent >= 85)
        {
            return new CapacityRecommendation
            {
                Status = "ScaleUp",
                Summary = "Workload is approaching its configured limit.",
                Reason = $"CPU {cpuUsagePercent:F1}% and memory {memoryUsagePercent:F1}% of limit.",
                SuggestedCpuLimitCores = snapshot.CpuLimitCores < 1
                    ? snapshot.CpuLimitCores + 0.25
                    : snapshot.CpuLimitCores + 0.50,
                SuggestedMemoryLimitMb = snapshot.MemoryLimitMb < 1024
                    ? snapshot.MemoryLimitMb + 256
                    : snapshot.MemoryLimitMb + 512
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
}