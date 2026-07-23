namespace Pat.Containers.CapacityAdvisor.Models
{
    public sealed class AksNodeSnapshot
    {
        public string NodeName { get; set; } = "";
        public double CpuAllocatableCores { get; set; }
        public double MemoryAllocatableMb { get; set; }
        public double CpuRequestedCores { get; set; }
        public double MemoryRequestedMb { get; set; }
        public double CpuUsageCores { get; set; }
        public double MemoryUsageMb { get; set; }

        public double FreeCpuByRequests => Math.Max(0, CpuAllocatableCores - CpuRequestedCores);
        public double FreeMemoryByRequestsMb => Math.Max(0, MemoryAllocatableMb - MemoryRequestedMb);
    }
}
