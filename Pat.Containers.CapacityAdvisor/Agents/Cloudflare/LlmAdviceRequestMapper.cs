using Pat.Containers.CapacityAdvisor.Enums;
using Pat.Containers.CapacityAdvisor.Models;

namespace Pat.Containers.CapacityAdvisor.Agents.Cloudflare
{
    public static class LlmAdviceRequestMapper
    {
        public static LlmAdviceRequest Map(
            PlatformSnapshot snapshot,
            string deterministicStatus,
            string deterministicReason)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            if (snapshot is AksPlatformSnapshot aksSnapshot)
            {
                return MapAks(aksSnapshot, deterministicStatus, deterministicReason);
            }

            return MapGeneric(snapshot, deterministicStatus, deterministicReason);
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
                CpuUsagePercent = CalculatePercent(snapshot.CpuUsageCores, snapshot.CpuLimitCores),
                MemoryUsagePercent = CalculatePercent(snapshot.MemoryUsageMb, snapshot.MemoryLimitMb),
                CpuRequestCores = snapshot.CpuRequestCores,
                CpuLimitCores = snapshot.CpuLimitCores,
                MemoryRequestMb = snapshot.MemoryRequestMb,
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
            var request = new LlmAdviceRequest
            {
                Platform = snapshot.Platform,
                WorkloadName = snapshot.WorkloadName,
                CurrentReplicas = snapshot.CurrentReplicas,
                CpuUsagePercent = CalculatePercent(snapshot.CpuUsageCores, snapshot.CpuLimitCores),
                MemoryUsagePercent = CalculatePercent(snapshot.MemoryUsageMb, snapshot.MemoryLimitMb),
                CpuRequestCores = snapshot.CpuRequestCores,
                CpuLimitCores = snapshot.CpuLimitCores,
                MemoryRequestMb = snapshot.MemoryRequestMb,
                MemoryLimitMb = snapshot.MemoryLimitMb,
                DeterministicStatus = deterministicStatus,
                DeterministicReason = deterministicReason,
                AdviceMode = snapshot.AdviceMode.ToString(),
                CanAssessNodeFit = snapshot.Placement.CanAssessNodeFit,
                CanAssessNeedForNewNode = snapshot.Placement.CanAssessNeedForNewNode,
                FitsExistingNode = snapshot.Placement.FitsExistingNode,
                NeedsNewNode = snapshot.Placement.NeedsNewNode,
                RecommendedNode = snapshot.Placement.RecommendedNode,
                Nodes = snapshot.Nodes
                    .Select(n => new LlmNodeAdviceInput
                    {
                        NodeName = n.NodeName,
                        CpuAllocatableCores = n.CpuAllocatableCores,
                        MemoryAllocatableMb = n.MemoryAllocatableMb,
                        CpuRequestedCores = n.CpuRequestedCores,
                        MemoryRequestedMb = n.MemoryRequestedMb,
                        FreeCpuByRequests = n.FreeCpuByRequests,
                        FreeMemoryByRequestsMb = n.FreeMemoryByRequestsMb
                    })
                    .ToList()
            };

            if (snapshot.AdviceMode == AksAdviceMode.LimitOnly)
            {
                request.CanAssessNodeFit = false;
                request.CanAssessNeedForNewNode = false;
                request.FitsExistingNode = false;
                request.NeedsNewNode = false;
                request.RecommendedNode = null;
                request.Nodes.Clear();
            }

            return request;
        }

        private static double CalculatePercent(double usage, double limit)
        {
            if (usage <= 0 || limit <= 0)
            {
                return 0;
            }

            return usage / limit * 100d;
        }
    }
}
