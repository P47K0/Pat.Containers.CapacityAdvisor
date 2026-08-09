using Pat.Containers.CapacityAdvisor.Enums;
using Pat.Containers.CapacityAdvisor.Models;
using Pat.Containers.CapacityAdvisor.Storage;

namespace Pat.Containers.CapacityAdvisor.Agents.Cloudflare;

public static class LlmAdviceRequestMapper
{
    public static LlmAdviceRequest Map(
        PlatformSnapshot snapshot,
        string deterministicStatus,
        string deterministicReason,
        CapacityStatusEntity? storedStatus = null,
        IReadOnlyList<AlertHistoryEntity>? recentAlerts = null,
        CapacityRecommendation? recommendation = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var request = snapshot is AksPlatformSnapshot aksSnapshot
            ? MapAks(
                aksSnapshot,
                deterministicStatus,
                deterministicReason)
            : MapGeneric(
                snapshot,
                deterministicStatus,
                deterministicReason);

        ApplyAdditionalContext(
            request,
            snapshot,
            recentAlerts,
            recommendation);

        return request;
    }

    private static LlmAdviceRequest MapGeneric(
        PlatformSnapshot snapshot,
        string deterministicStatus,
        string deterministicReason)
    {
        return new LlmAdviceRequest
        {
            Platform = snapshot.Platform,
            WorkloadName = snapshot.WorkloadName,
            CurrentReplicas = snapshot.CurrentReplicas,

            CpuUsagePercentOfLimit =
                CalculatePercent(
                    snapshot.CpuUsageCores,
                    snapshot.CpuLimitCores),

            MemoryUsagePercentOfLimit =
                CalculatePercent(
                    snapshot.MemoryUsageMb,
                    snapshot.MemoryLimitMb),

            CpuUsageCores = snapshot.CpuUsageCores,
            MemoryUsageMb = snapshot.MemoryUsageMb,

            CpuRequestCores = snapshot.CpuRequestCores,
            MemoryRequestMb = snapshot.MemoryRequestMb,

            CpuLimitCores = snapshot.CpuLimitCores,
            MemoryLimitMb = snapshot.MemoryLimitMb,

            DeterministicStatus = deterministicStatus,
            DeterministicReason = deterministicReason
        };
    }

    private static LlmAdviceRequest MapAks(
        AksPlatformSnapshot snapshot,
        string deterministicStatus,
        string deterministicReason)
    {
        var placement = snapshot.Placement;

        var request = new LlmAdviceRequest
        {
            Platform = snapshot.Platform,
            WorkloadName = snapshot.WorkloadName,
            CurrentReplicas = snapshot.CurrentReplicas,

            CpuUsagePercentOfLimit =
                CalculatePercent(
                    snapshot.CpuUsageCores,
                    snapshot.CpuLimitCores),

            MemoryUsagePercentOfLimit =
                CalculatePercent(
                    snapshot.MemoryUsageMb,
                    snapshot.MemoryLimitMb),

            CpuUsageCores = snapshot.CpuUsageCores,
            MemoryUsageMb = snapshot.MemoryUsageMb,

            CpuRequestCores = snapshot.CpuRequestCores,
            MemoryRequestMb = snapshot.MemoryRequestMb,

            CpuLimitCores = snapshot.CpuLimitCores,
            MemoryLimitMb = snapshot.MemoryLimitMb,

            DeterministicStatus = deterministicStatus,
            DeterministicReason = deterministicReason,

            AdviceMode = snapshot.AdviceMode.ToString(),

            CanAssessNodeFit =
                placement?.CanAssessNodeFit ?? false,

            CanAssessNeedForNewNode =
                placement?.CanAssessNeedForNewNode ?? false,

            FitsExistingNode =
                placement?.FitsExistingNode ?? false,

            NeedsNewNode =
                placement?.NeedsNewNode ?? false,

            RecommendedNode =
                placement?.RecommendedNode,

            PlacementReason =
                placement?.Reason,

            PlacementRiskLevel =
                placement?.RiskLevel,

            ClusterCapacity =
                snapshot.ClusterCapacity,

            Nodes = snapshot.Nodes
                .Select(MapNode)
                .ToList()
        };

        if (snapshot.AdviceMode == AksAdviceMode.LimitOnly)
        {
            request.CanAssessNodeFit = false;
            request.CanAssessNeedForNewNode = false;
            request.FitsExistingNode = false;
            request.NeedsNewNode = false;
            request.RecommendedNode = null;
            request.PlacementReason = null;
            request.PlacementRiskLevel = null;
            request.ClusterCapacity = null;
            request.Nodes.Clear();
        }

        return request;
    }

    private static LlmNodeAdviceInput MapNode(
        AksNodeSnapshot node)
    {
        return new LlmNodeAdviceInput
        {
            NodeName = node.NodeName,

            CpuAllocatableCores =
                node.CpuAllocatableCores,

            MemoryAllocatableMb =
                node.MemoryAllocatableMb,

            CpuUsageCores =
                node.CpuUsageCores,

            MemoryUsageMb =
                node.MemoryUsageMb,

            CpuRequestedCores =
                node.CpuRequestedCores,

            MemoryRequestedMb =
                node.MemoryRequestedMb,

            CpuLimitsCores =
                node.CpuLimitsCores,

            MemoryLimitsMb =
                node.MemoryLimitsMb,

            FreeCpuByRequests =
                node.FreeCpuByRequests,

            FreeMemoryByRequestsMb =
                node.FreeMemoryByRequestsMb,

            FreeCpuByLiveUsage =
                node.FreeCpuByLiveUsage,

            FreeMemoryByLiveUsageMb =
                node.FreeMemoryByLiveUsageMb,

            CpuRequestSaturationPercent =
                node.CpuRequestSaturationPercent,

            MemoryRequestSaturationPercent =
                node.MemoryRequestSaturationPercent,

            CpuLiveUsagePercent =
                node.CpuLiveUsagePercent,

            MemoryLiveUsagePercent =
                node.MemoryLiveUsagePercent,

            CpuLimitOvercommitPercent =
                node.CpuLimitOvercommitPercent,

            MemoryLimitOvercommitPercent =
                node.MemoryLimitOvercommitPercent,

            Ready = node.Ready,
            Schedulable = node.Schedulable
        };
    }

    private static void ApplyAdditionalContext(
        LlmAdviceRequest request,
        PlatformSnapshot snapshot,
        IReadOnlyList<AlertHistoryEntity>? recentAlerts,
        CapacityRecommendation? recommendation)
    {
        if (recommendation is not null)
        {
            request.DeterministicSummary =
                recommendation.Summary;

            request.DeterministicRecommendedAction =
                recommendation.RecommendedAction;

            request.SuggestedCpuLimitCores =
                recommendation.SuggestedCpuLimitCores;

            request.SuggestedMemoryLimitMb =
                recommendation.SuggestedMemoryLimitMb;
        }

        if (recentAlerts is not null)
        {
            request.HistoricalAlerts =
                BuildAlertSummaries(
                    recentAlerts);
        }
    }

    private static IReadOnlyList<LlmAlertSummary>
    BuildAlertSummaries(
        IReadOnlyList<AlertHistoryEntity>? recentAlerts)
    {
        if (recentAlerts is null ||
            recentAlerts.Count == 0)
        {
            return Array.Empty<LlmAlertSummary>();
        }

        return recentAlerts
            .GroupBy(
                alert => GetAlertCategory(alert),
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
                new LlmAlertSummary
                {
                    Category = group.Key,

                    Count = group.Count(),

                    RecentValuesPercent =
                        group
                            .OrderByDescending(
                                alert => alert.FiredAtUtc)
                            .Where(
                                alert =>
                                    alert.ObservedValue.HasValue)
                            .Take(5)
                            .Select(
                                alert =>
                                    Math.Round(
                                        alert.ObservedValue!.Value,
                                        1))
                            .ToList()
                })
            .ToList();
    }

    private static string GetAlertCategory(
    AlertHistoryEntity alert)
    {
        if (!string.IsNullOrWhiteSpace(
                alert.RuleName))
        {
            return alert.RuleName;
        }

        if (!string.IsNullOrWhiteSpace(
                alert.SignalType))
        {
            return alert.SignalType;
        }

        return "Unknown";
    }

    private static double CalculatePercent(
        double usage,
        double limit)
    {
        if (usage <= 0 || limit <= 0)
        {
            return 0;
        }

        return usage / limit * 100d;
    }
}