using System.Text.Json;

namespace Pat.Containers.CapacityAdvisor.Models.Webhook
{
    public sealed class AzureMonitorCommonAlertData
    {
        public AzureMonitorAlertEssentials? Essentials { get; set; }
        public JsonElement AlertContext { get; set; }
    }
}
