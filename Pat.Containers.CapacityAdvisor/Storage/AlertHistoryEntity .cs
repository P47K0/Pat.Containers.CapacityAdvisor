using Azure;
using Azure.Data.Tables;

namespace Pat.Containers.CapacityAdvisor.Storage;

public sealed class AlertHistoryEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string ExternalAlertId { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public string MonitorCondition { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string MonitoringService { get; set; } = string.Empty;
    public string SignalType { get; set; } = string.Empty;
    public string ClusterName { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
    public string WorkloadName { get; set; } = string.Empty;
    public DateTimeOffset FiredAtUtc { get; set; }
    public double? ObservedValue { get; set; }
    public string RawPayloadJson { get; set; } = string.Empty;
}