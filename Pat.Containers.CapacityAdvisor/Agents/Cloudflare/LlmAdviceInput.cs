namespace Pat.Containers.CapacityAdvisor.Agents.Cloudflare
{
    public sealed class LlmNodeAdviceInput
    {
        public string NodeName { get; set; } = "";
        public double CpuAllocatableCores { get; set; }
        public double MemoryAllocatableMb { get; set; }
        public double CpuRequestedCores { get; set; }
        public double MemoryRequestedMb { get; set; }
        public double FreeCpuByRequests { get; set; }
        public double FreeMemoryByRequestsMb { get; set; }
    }
}
