namespace Pat.Containers.CapacityAdvisor.Models;

public sealed class AksNodeSnapshot
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

    public bool Ready { get; set; } = true;
    public bool Schedulable { get; set; } = true;

    public double FreeCpuByRequests => Math.Max(0, CpuAllocatableCores - CpuRequestedCores);
    public double FreeMemoryByRequestsMb => Math.Max(0, MemoryAllocatableMb - MemoryRequestedMb);

    public double FreeCpuByLiveUsage => Math.Max(0, CpuAllocatableCores - CpuUsageCores);
    public double FreeMemoryByLiveUsageMb => Math.Max(0, MemoryAllocatableMb - MemoryUsageMb);

    public double CpuRequestSaturationPercent =>
        CpuAllocatableCores <= 0 ? 0 : (CpuRequestedCores / CpuAllocatableCores) * 100d;

    public double MemoryRequestSaturationPercent =>
        MemoryAllocatableMb <= 0 ? 0 : (MemoryRequestedMb / MemoryAllocatableMb) * 100d;

    public double CpuLiveUsagePercent =>
        CpuAllocatableCores <= 0 ? 0 : (CpuUsageCores / CpuAllocatableCores) * 100d;

    public double MemoryLiveUsagePercent =>
        MemoryAllocatableMb <= 0 ? 0 : (MemoryUsageMb / MemoryAllocatableMb) * 100d;

    public double CpuLimitOvercommitPercent =>
        CpuAllocatableCores <= 0 ? 0 : (CpuLimitsCores / CpuAllocatableCores) * 100d;

    public double MemoryLimitOvercommitPercent =>
        MemoryAllocatableMb <= 0 ? 0 : (MemoryLimitsMb / MemoryAllocatableMb) * 100d;

    public double ProjectedCpuRequestSaturationPercent(double additionalCpuRequestCores) =>
        CpuAllocatableCores <= 0
            ? 100d
            : ((CpuRequestedCores + additionalCpuRequestCores) / CpuAllocatableCores) * 100d;

    public double ProjectedMemoryRequestSaturationPercent(double additionalMemoryRequestMb) =>
        MemoryAllocatableMb <= 0
            ? 100d
            : ((MemoryRequestedMb + additionalMemoryRequestMb) / MemoryAllocatableMb) * 100d;

    public double ProjectedCpuLiveUsagePercent(double additionalCpuUsageCores) =>
        CpuAllocatableCores <= 0
            ? 100d
            : ((CpuUsageCores + additionalCpuUsageCores) / CpuAllocatableCores) * 100d;

    public double ProjectedMemoryLiveUsagePercent(double additionalMemoryUsageMb) =>
        MemoryAllocatableMb <= 0
            ? 100d
            : ((MemoryUsageMb + additionalMemoryUsageMb) / MemoryAllocatableMb) * 100d;
}