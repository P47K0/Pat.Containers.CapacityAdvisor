using Pat.Containers.CapacityAdvisor.Models.Webhook;

namespace Pat.Containers.CapacityAdvisor.Repositories
{
    public interface IAlertEventRepository
    {
        Task<bool> ExistsAsync(
            string externalAlertId,
            string monitorCondition,
            DateTimeOffset firedAtUtc,
            CancellationToken cancellationToken);

        Task AddAsync(AlertEvent alert, CancellationToken cancellationToken);

        Task<IReadOnlyList<AlertEvent>> GetRecentMatchingAlertsAsync(
            string clusterName,
            string @namespace,
            string workloadName,
            string signalType,
            int take,
            CancellationToken cancellationToken);
    }
}
