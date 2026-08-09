namespace Pat.Containers.CapacityAdvisor.Storage;

public interface IAlertHistoryRepository
{
    Task<bool> ExistsAsync(string partitionKey, string externalAlertId, string monitorCondition, DateTimeOffset firedAtUtc, CancellationToken cancellationToken);
    Task AddAsync(AlertHistoryEntity entity, CancellationToken cancellationToken);
    Task<IReadOnlyList<AlertHistoryEntity>> GetRecentAsync(string partitionKey, int take, string? signalType, CancellationToken cancellationToken);
    Task<IReadOnlyList<AlertHistoryEntity>> GetRecentForWorkloadAsync(string clusterName, string @namespace, string workloadName, int take, CancellationToken cancellationToken);
}