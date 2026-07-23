namespace Pat.Containers.CapacityAdvisor.Models;

public class PlatformSnapshot
{
    public string Platform { get; init; } = default!;
    public DateTimeOffset CollectedAtUtc { get; init; }

    public string EnvironmentName { get; init; } = default!;
    public string WorkloadName { get; init; } = default!;

    public int CurrentReplicas { get; init; }

    public double CpuUsageCores { get; init; }
    public double CpuRequestCores { get; init; }
    public double CpuLimitCores { get; init; }

    public double MemoryUsageMb { get; init; }
    public double MemoryRequestMb { get; init; }
    public double MemoryLimitMb { get; init; }



    public Dictionary<string, string> Metadata { get; init; } = new();
    public string ResourceId { get; internal set; }
}