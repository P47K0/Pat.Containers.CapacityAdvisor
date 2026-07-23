namespace Pat.Containers.CapacityAdvisor.Agents.Cloudflare;

public sealed class LlmAdviceRequest
{
    public string Platform { get; init; } = default!;
    public string WorkloadName { get; init; } = default!;

    public int CurrentReplicas { get; init; }

    public double CpuUsagePercent { get; init; }
    public double MemoryUsagePercent { get; init; }

    public double CpuRequestCores { get; init; }
    public double CpuLimitCores { get; init; }

    public double MemoryRequestMb { get; init; }
    public double MemoryLimitMb { get; init; }

    public string DeterministicStatus { get; init; } = default!;
    public string DeterministicReason { get; init; } = default!;

    public string? AdviceMode { get; set; }
    public bool CanAssessNodeFit { get; set; }
    public bool CanAssessNeedForNewNode { get; set; }
    public bool FitsExistingNode { get; set; }
    public bool NeedsNewNode { get; set; }
    public string? RecommendedNode { get; set; }
    public List<LlmNodeAdviceInput> Nodes { get; set; } = [];
}