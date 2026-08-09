using Azure.Data.Tables;

namespace Pat.Containers.CapacityAdvisor.Storage;

public sealed class TableCapacityStatusRepository : ICapacityStatusRepository
{
    private readonly TableClient _tableClient;

    public TableCapacityStatusRepository(TableClient tableClient)
    {
        _tableClient = tableClient;
    }

    public async Task<CapacityStatusEntity?> GetAsync(string partitionKey, CancellationToken cancellationToken)
    {
        var filter = $"PartitionKey eq '{Escape(partitionKey)}' and RowKey eq 'current'";

        await foreach (var entity in _tableClient.QueryAsync<CapacityStatusEntity>(filter: filter, cancellationToken: cancellationToken))
        {
            return entity;
        }

        return null;
    }

    public Task UpsertAsync(CapacityStatusEntity entity, CancellationToken cancellationToken)
    {
        return _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);
    }

    private static string Escape(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);
}