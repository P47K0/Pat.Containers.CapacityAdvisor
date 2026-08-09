namespace Pat.Containers.CapacityAdvisor.Models.Webhook
{
    public sealed class AzureMonitorCommonAlert
    {
        public string? SchemaId { get; set; }
        public AzureMonitorCommonAlertData? Data { get; set; }
    }
}
