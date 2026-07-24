namespace Pat.Containers.CapacityAdvisor.Models;

public sealed class LlmNodeAdviceInput
{
    public string NodeName { get; set; } = string.Empty;

    public double CpuAllocatableCores { get; set; }
    public double MemoryAllocatableMb { get; set; }

    public double CpuUsageCores { get; set; }
    public double MemoryUsageMb { get; set; }

    public double CpuRequestedCores { get; set; }
    public double MemoryRequestedMb { get; set; }

    public double CpuLimitsCores { get; set; }
    public double MemoryLimitsMb { get; set; }

    public double FreeCpuByRequests { get; set; }
    public double FreeMemoryByRequestsMb { get; set; }

    public double FreeCpuByLiveUsage { get; set; }
    public double FreeMemoryByLiveUsageMb { get; set; }

    public double CpuRequestSaturationPercent { get; set; }
    public double MemoryRequestSaturationPercent { get; set; }

    public double CpuLiveUsagePercent { get; set; }
    public double MemoryLiveUsagePercent { get; set; }

    public double CpuLimitOvercommitPercent { get; set; }
    public double MemoryLimitOvercommitPercent { get; set; }

    public bool Ready { get; set; }
    public bool Schedulable { get; set; }
}