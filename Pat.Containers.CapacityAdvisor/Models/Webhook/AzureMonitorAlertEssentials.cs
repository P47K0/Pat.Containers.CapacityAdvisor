namespace Pat.Containers.CapacityAdvisor.Models.Webhook
{
    public sealed class AzureMonitorAlertEssentials
    {
        public string? AlertId { get; set; }
        public string? AlertRule { get; set; }
        public string? Severity { get; set; }
        public string? SignalType { get; set; }
        public string? MonitorCondition { get; set; }
        public string? MonitoringService { get; set; }
        public string[]? AlertTargetIDs { get; set; }
        public DateTimeOffset FiredDateTime { get; set; }
        public DateTimeOffset? ResolvedDateTime { get; set; }
        public string? Description { get; set; }
    }
}
