namespace Pat.Containers.CapacityAdvisor.Platform.Aks
{
    public sealed class AksMetricCollectorOptions
    {
        public const string SectionName = "PlatformCollectors:AksMetrics";

        public string SubscriptionId { get; set; } = "";
        public string ResourceGroup { get; set; } = "";
        public string ClusterName { get; set; } = "";
        public string Namespace { get; set; } = "default";
        public string WorkloadName { get; set; } = "";
        public string WorkloadKind { get; set; } = "Deployment";
        public string? ContainerName { get; set; }
        public string? PrometheusQueryEndpoint { get; set; }
        public int MetricWindowMinutes { get; set; } = 30;
        public double LimitPressureThreshold { get; set; } = 0.85;

        public double CurrentCpuLimitCores { get; set; }
        public double CurrentMemoryLimitMb { get; set; }
    }
}
