using Azure;
using Azure.Data.Tables;

public sealed class CapacityStatusEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;

    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string ClusterName { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
    public string WorkloadName { get; set; } = string.Empty;
    public string SignalType { get; set; } = string.Empty;

    // Historical information used to understand the trend.
    public string? TrendState { get; set; }
    public int AlertCount { get; set; }
}