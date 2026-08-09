using Azure;
using Azure.Data.Tables;

namespace Pat.Containers.CapacityAdvisor.Storage;

public sealed class TableAlertHistoryRepository : IAlertHistoryRepository
{
    private readonly TableClient _tableClient;

    public TableAlertHistoryRepository(
        TableClient tableClient)
    {
        _tableClient = tableClient;
    }

    public async Task<bool> ExistsAsync(
        string partitionKey,
        string externalAlertId,
        string monitorCondition,
        DateTimeOffset firedAtUtc,
        CancellationToken cancellationToken)
    {
        var filter =
            $"PartitionKey eq '{Escape(partitionKey)}' " +
            $"and ExternalAlertId eq '{Escape(externalAlertId)}' " +
            $"and MonitorCondition eq '{Escape(monitorCondition)}' " +
            $"and FiredAtUtc eq datetime'{firedAtUtc:O}'";

        await foreach (var _ in _tableClient.QueryAsync<AlertHistoryEntity>(
            filter: filter,
            cancellationToken: cancellationToken))
        {
            return true;
        }

        return false;
    }

    public Task AddAsync(
        AlertHistoryEntity entity,
        CancellationToken cancellationToken)
    {
        ValidateKey(entity.PartitionKey, nameof(entity.PartitionKey));
        ValidateKey(entity.RowKey, nameof(entity.RowKey));

        return _tableClient.AddEntityAsync(
            entity,
            cancellationToken);
    }

    public async Task<IReadOnlyList<AlertHistoryEntity>> GetRecentAsync(
    string partitionKey,
    int take,
    string? signalType,
    CancellationToken cancellationToken)
    {
        var filter =
            $"PartitionKey eq '{Escape(partitionKey)}'";

        if (!string.IsNullOrWhiteSpace(signalType))
        {
            filter +=
                $" and SignalType eq '{Escape(signalType)}'";
        }

        var results = new List<AlertHistoryEntity>();

        await foreach (var entity in _tableClient.QueryAsync<AlertHistoryEntity>(
            filter: filter,
            cancellationToken: cancellationToken))
        {
            // The current/recommendation row does not have FiredAtUtc.
            if (entity.FiredAtUtc == default)
            {
                continue;
            }

            results.Add(entity);
        }

        return results
            .OrderByDescending(entity => entity.FiredAtUtc)
            .Take(take)
            .OrderBy(entity => entity.FiredAtUtc)
            .ToList();
    }

    public Task<IReadOnlyList<AlertHistoryEntity>>
        GetRecentForWorkloadAsync(
            string clusterName,
            string @namespace,
            string workloadName,
            int take,
            CancellationToken cancellationToken)
    {
        var partitionKey =
            $"{clusterName}|{@namespace}|{workloadName}";

        return GetRecentAsync(
            partitionKey,
            take,
            signalType: null,
            cancellationToken);
    }

    private static void ValidateKey(
        string value,
        string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"{name} cannot be empty.");
        }

        if (value.Contains('/',
                StringComparison.Ordinal) ||
            value.Contains('\\',
                StringComparison.Ordinal) ||
            value.Contains('#',
                StringComparison.Ordinal) ||
            value.Contains('?',
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{name} contains a character that is not allowed " +
                "in Azure Table Storage keys.");
        }
    }

    private static string Escape(
        string value)
    {
        return value.Replace(
            "'",
            "''",
            StringComparison.Ordinal);
    }
}