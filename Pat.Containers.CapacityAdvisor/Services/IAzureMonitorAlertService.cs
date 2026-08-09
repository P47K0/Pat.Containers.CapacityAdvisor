using Pat.Containers.CapacityAdvisor.Models.Webhook;

namespace Pat.Containers.CapacityAdvisor.Services
{
    public interface IAzureMonitorAlertService
    {
        Task HandleAsync(AzureMonitorCommonAlert payload, CancellationToken cancellationToken);
    }
}
