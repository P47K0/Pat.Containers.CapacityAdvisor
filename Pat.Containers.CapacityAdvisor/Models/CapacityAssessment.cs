namespace Pat.Containers.CapacityAdvisor.Models;

public sealed class CapacityAssessment
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public PlatformSnapshot? Snapshot { get; init; }

    public double CpuUsagePercent { get; init; }
    public double MemoryUsagePercent { get; init; }

    public CapacityRecommendation Recommendation { get; init; } = CapacityRecommendation.Unknown("No assessment available.");

    public LlmAdviceResponse? LlmAdvice { get; init; }
}