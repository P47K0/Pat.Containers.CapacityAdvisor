namespace Pat.Containers.CapacityAdvisor.Models;

public sealed class LlmAdviceRequest
{
    public string Platform { get; set; } = string.Empty;
    public string WorkloadName { get; set; } = string.Empty;

    public int CurrentReplicas { get; set; }

    public string? AdviceMode { get; set; }

    public double CpuUsagePercent { get; set; }
    public double MemoryUsagePercent { get; set; }

    public double CpuUsageCores { get; set; }
    public double MemoryUsageMb { get; set; }

    public double CpuRequestCores { get; set; }
    public double MemoryRequestMb { get; set; }

    public double CpuLimitCores { get; set; }
    public double MemoryLimitMb { get; set; }

    public bool CanAssessNodeFit { get; set; }
    public bool CanAssessNeedForNewNode { get; set; }
    public bool FitsExistingNode { get; set; }
    public bool NeedsNewNode { get; set; }

    public string? RecommendedNode { get; set; }

    public string DeterministicStatus { get; set; } = string.Empty;
    public string DeterministicReason { get; set; } = string.Empty;

    public AksClusterCapacitySnapshot? ClusterCapacity { get; set; }

    public List<LlmNodeAdviceInput> Nodes { get; set; } = [];
}