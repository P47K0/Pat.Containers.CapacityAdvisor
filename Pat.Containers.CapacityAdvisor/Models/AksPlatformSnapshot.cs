using Pat.Containers.CapacityAdvisor.Enums;

namespace Pat.Containers.CapacityAdvisor.Models
{
    public sealed class AksPlatformSnapshot : PlatformSnapshot
    {
        public AksAdviceMode AdviceMode { get; set; }
        public string ClusterName { get; set; } = "";
        public string Namespace { get; set; } = "";
        public List<AksNodeSnapshot> Nodes { get; set; } = [];
        public AksPlacementAdvice Placement { get; set; } = new();
    }
}
