namespace Pat.Containers.CapacityAdvisor.Models;

public sealed class CapacityRecommendation
{
    public string Status { get; init; } = default!;
    public string Summary { get; init; } = default!;
    public string Reason { get; init; } = default!;

    public double SuggestedCpuLimitCores { get; init; }
    public double SuggestedMemoryLimitMb { get; init; }

    public static CapacityRecommendation Unknown(string reason) =>
        new()
        {
            Status = "Unknown",
            Summary = "Unable to determine workload health.",
            Reason = reason
        };
}