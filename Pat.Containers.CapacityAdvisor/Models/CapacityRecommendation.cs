using Pat.Containers.CapacityAdvisor.Storage;

namespace Pat.Containers.CapacityAdvisor.Models;

public sealed class CapacityRecommendation
{
    public string Status { get; init; } = default!;
    public string Summary { get; init; } = default!;
    public string Reason { get; init; } = default!;
    public string RecommendedAction { get; init; } = default!;

    public double SuggestedCpuLimitCores { get; init; }
    public double SuggestedMemoryLimitMb { get; init; }

    public static CapacityRecommendation Create(
    CapacityStatusEntity alertStatus,
    PlatformSnapshot currentSnapshot)
    {
        if (currentSnapshot is AksPlatformSnapshot aksSnapshot)
        {
            return CreateForAks(
                alertStatus,
                aksSnapshot);
        }

        return CreateForAca(
            alertStatus,
            currentSnapshot);
    }

    private static CapacityRecommendation CreateForAks(
    CapacityStatusEntity alertStatus,
    AksPlatformSnapshot snapshot)
    {
        var placement = snapshot.Placement;

        if (placement.NeedsNewNode)
        {
            return CapacityRecommendation.ScaleOut(
                summary:
                    "The workload does not have sufficient capacity on the existing AKS nodes.",
                reason:
                    placement.Reason,
                suggestedCpuLimitCores:
                    snapshot.CpuLimitCores,
                suggestedMemoryLimitMb:
                    snapshot.MemoryLimitMb);
        }

        if (placement.ShouldIncreaseMemoryLimit)
        {
            return CapacityRecommendation.IncreaseMemoryLimit(
                summary:
                    "Memory pressure is increasing, but the workload still fits on an existing AKS node.",
                reason:
                    placement.Reason,
                suggestedCpuLimitCores:
                    snapshot.CpuLimitCores,
                suggestedMemoryLimitMb:
                    snapshot.MemoryLimitMb);
        }

        return CapacityRecommendation.FitsExistingNode(
            summary:
                "The workload fits on an existing AKS node.",
            reason:
                placement.Reason,
            suggestedCpuLimitCores:
                snapshot.CpuLimitCores,
            suggestedMemoryLimitMb:
                snapshot.MemoryLimitMb);
    }

    private static CapacityRecommendation CreateForAca(
    CapacityStatusEntity alertStatus,
    PlatformSnapshot snapshot)
    {
        var trendState = alertStatus.TrendState ?? "Unknown";

        var memoryPressure =
            snapshot.MemoryLimitMb > 0 &&
            snapshot.MemoryUsageMb >= snapshot.MemoryLimitMb * 0.80;

        if (memoryPressure)
        {
            return CapacityRecommendation.IncreaseMemoryLimit(
                summary:
                    "Memory pressure is increasing on the workload.",
                reason:
                    $"The alert trend is {trendState}. " +
                    $"Current memory usage is {snapshot.MemoryUsageMb:F0} MB " +
                    $"of a {snapshot.MemoryLimitMb:F0} MB limit.",
                suggestedCpuLimitCores:
                    snapshot.CpuLimitCores,
                suggestedMemoryLimitMb:
                    snapshot.MemoryLimitMb);
        }

        return CapacityRecommendation.FitsExistingNode(
            summary:
                "The workload is within its configured resource limits.",
            reason:
                $"The alert trend is {trendState}. " +
                $"Current CPU usage is {snapshot.CpuUsageCores:F2} cores " +
                $"and memory usage is {snapshot.MemoryUsageMb:F0} MB.",
            suggestedCpuLimitCores:
                snapshot.CpuLimitCores,
            suggestedMemoryLimitMb:
                snapshot.MemoryLimitMb);
    }

    private static CapacityRecommendation CreateForGenericPlatform(
    CapacityStatusEntity alertStatus,
    PlatformSnapshot snapshot)
    {
        var trendState = alertStatus.TrendState ?? "Unknown";

        var memoryPressure =
            snapshot.MemoryLimitMb > 0 &&
            snapshot.MemoryUsageMb >= snapshot.MemoryLimitMb * 0.80;

        if (memoryPressure)
        {
            return CapacityRecommendation.IncreaseMemoryLimit(
                summary:
                    "Memory usage is high on the workload.",
                reason:
                    $"The alert trend is {trendState}. " +
                    $"Current memory usage is {snapshot.MemoryUsageMb:F0} MB " +
                    $"of a {snapshot.MemoryLimitMb:F0} MB limit.",
                suggestedCpuLimitCores:
                    snapshot.CpuLimitCores,
                suggestedMemoryLimitMb:
                    snapshot.MemoryLimitMb);
        }

        return CapacityRecommendation.FitsExistingNode(
            summary:
                "The workload is within its configured resource limits.",
            reason:
                $"The alert trend is {trendState}. " +
                $"Current CPU usage is {snapshot.CpuUsageCores:F2} cores " +
                $"and memory usage is {snapshot.MemoryUsageMb:F0} MB.",
            suggestedCpuLimitCores:
                snapshot.CpuLimitCores,
            suggestedMemoryLimitMb:
                snapshot.MemoryLimitMb);
    }

    public static CapacityRecommendation Unknown(string reason) =>
        new()
        {
            Status = "Unknown",
            Summary = "Unable to determine workload health.",
            Reason = reason,
            RecommendedAction = "Review workload and cluster capacity manually."
        };

    public static CapacityRecommendation ScaleOut(
        string summary,
        string reason,
        double suggestedCpuLimitCores,
        double suggestedMemoryLimitMb) =>
        new()
        {
            Status = "NeedsNewNode",
            Summary = summary,
            Reason = reason,
            RecommendedAction =
                "Add an extra node or move the workload to a less saturated node.",
            SuggestedCpuLimitCores = suggestedCpuLimitCores,
            SuggestedMemoryLimitMb = suggestedMemoryLimitMb
        };

    public static CapacityRecommendation IncreaseMemoryLimit(
        string summary,
        string reason,
        double suggestedCpuLimitCores,
        double suggestedMemoryLimitMb) =>
        new()
        {
            Status = "IncreaseMemoryLimit",
            Summary = summary,
            Reason = reason,
            RecommendedAction =
                "Review and increase the workload memory limit, then continue monitoring.",
            SuggestedCpuLimitCores = suggestedCpuLimitCores,
            SuggestedMemoryLimitMb = suggestedMemoryLimitMb
        };

    public static CapacityRecommendation FitsExistingNode(
        string summary,
        string reason,
        double suggestedCpuLimitCores,
        double suggestedMemoryLimitMb) =>
        new()
        {
            Status = "FitsExistingNode",
            Summary = summary,
            Reason = reason,
            RecommendedAction = "No immediate action required.",
            SuggestedCpuLimitCores = suggestedCpuLimitCores,
            SuggestedMemoryLimitMb = suggestedMemoryLimitMb
        };

    private static bool IsMemoryPressureIncreasing(
    CapacityStatusEntity alertStatus,
    PlatformSnapshot currentSnapshot)
    {
        var trendIsIncreasing =
            string.Equals(
                alertStatus.TrendState,
                "Increasing",
                StringComparison.OrdinalIgnoreCase)
            ||
            string.Equals(
                alertStatus.TrendState,
                "Worsening",
                StringComparison.OrdinalIgnoreCase);

        var memoryLimitReached =
            currentSnapshot.MemoryLimitMb > 0 &&
            currentSnapshot.MemoryUsageMb >=
            currentSnapshot.MemoryLimitMb * 0.80;

        return trendIsIncreasing || memoryLimitReached;
    }
}