namespace Pat.Containers.CapacityAdvisor.Models.Webhook
{
    public sealed class AlertEvent
    {
        public string ExternalAlertId { get; set; } = string.Empty;
        public string RuleName { get; set; } = string.Empty;
        public string MonitorCondition { get; set; } = string.Empty; // Fired/Resolved
        public string Severity { get; set; } = string.Empty;
        public string MonitoringService { get; set; } = string.Empty; // Prometheus
        public string SignalType { get; set; } = string.Empty; // HighMemoryUsage / CpuThrottling
        public string ClusterName { get; set; } = string.Empty;
        public string Namespace { get; set; } = string.Empty;
        public string WorkloadName { get; set; } = string.Empty;
        public DateTimeOffset FiredAtUtc { get; set; }
        public double? ObservedValue { get; set; }
        public string RawPayloadJson { get; set; } = string.Empty;
    }
}
