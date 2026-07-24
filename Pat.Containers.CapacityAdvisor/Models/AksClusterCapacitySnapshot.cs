namespace Pat.Containers.CapacityAdvisor.Models;

public sealed class AksClusterCapacitySnapshot
{
    public int TotalNodeCount { get; set; }
    public int ReadySchedulableNodeCount { get; set; }

    public double TotalCpuAllocatableCores { get; set; }
    public double TotalMemoryAllocatableMb { get; set; }

    public double TotalCpuUsageCores { get; set; }
    public double TotalMemoryUsageMb { get; set; }

    public double TotalCpuRequestedCores { get; set; }
    public double TotalMemoryRequestedMb { get; set; }

    public double TotalCpuLimitsCores { get; set; }
    public double TotalMemoryLimitsMb { get; set; }

    public double TotalFreeCpuByRequestsCores => Math.Max(0, TotalCpuAllocatableCores - TotalCpuRequestedCores);
    public double TotalFreeMemoryByRequestsMb => Math.Max(0, TotalMemoryAllocatableMb - TotalMemoryRequestedMb);

    public double TotalFreeCpuByLiveUsageCores => Math.Max(0, TotalCpuAllocatableCores - TotalCpuUsageCores);
    public double TotalFreeMemoryByLiveUsageMb => Math.Max(0, TotalMemoryAllocatableMb - TotalMemoryUsageMb);

    public double CpuRequestSaturationPercent =>
        TotalCpuAllocatableCores <= 0 ? 0 : (TotalCpuRequestedCores / TotalCpuAllocatableCores) * 100d;

    public double MemoryRequestSaturationPercent =>
        TotalMemoryAllocatableMb <= 0 ? 0 : (TotalMemoryRequestedMb / TotalMemoryAllocatableMb) * 100d;

    public double CpuLimitOvercommitPercent =>
        TotalCpuAllocatableCores <= 0 ? 0 : (TotalCpuLimitsCores / TotalCpuAllocatableCores) * 100d;

    public double MemoryLimitOvercommitPercent =>
        TotalMemoryAllocatableMb <= 0 ? 0 : (TotalMemoryLimitsMb / TotalMemoryAllocatableMb) * 100d;
}