namespace Pat.Containers.CapacityAdvisor.Models.Webhook
{
    public sealed class AlertTrendSummary
    {
        public int AlertCount { get; set; }
        public bool IsRecurring { get; set; }
        public bool IsWorsening { get; set; }
        public double? FirstValue { get; set; }
        public double? LastValue { get; set; }
        public string Summary { get; set; } = string.Empty;
    }
}
