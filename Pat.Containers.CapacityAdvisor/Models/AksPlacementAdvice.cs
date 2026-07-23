using Pat.Containers.CapacityAdvisor.Enums;

namespace Pat.Containers.CapacityAdvisor.Models
{
    public sealed class AksPlacementAdvice
    {
        public AksAdviceMode Mode { get; set; }
        public bool CanAssessNodeFit { get; set; }
        public bool CanAssessNeedForNewNode { get; set; }
        public bool FitsExistingNode { get; set; }
        public bool NeedsNewNode { get; set; }
        public string? RecommendedNode { get; set; }
        public bool ShouldIncreaseCpuLimit { get; set; }
        public bool ShouldIncreaseMemoryLimit { get; set; }
        public string RiskLevel { get; set; } = "Unknown";
        public string Reason { get; set; } = "";
    }

}
