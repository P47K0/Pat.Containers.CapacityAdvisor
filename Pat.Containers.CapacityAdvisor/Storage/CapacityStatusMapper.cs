using Pat.Containers.CapacityAdvisor.Models;
using Pat.Containers.CapacityAdvisor.Models.Webhook;
using Pat.Containers.CapacityAdvisor.Storage;

public static class CapacityStatusMapper
{
    public static CapacityStatusEntity Map(
        AlertHistoryEntity alert,
        AlertTrendSummary trend)
    {
        return new CapacityStatusEntity
        {
            PartitionKey = alert.PartitionKey,
            RowKey = "current",

            ClusterName = alert.ClusterName,
            Namespace = alert.Namespace,
            WorkloadName = alert.WorkloadName,
            SignalType = alert.SignalType,

            AlertCount = trend.AlertCount,
            TrendState = GetTrendState(trend)
        };
    }

    private static string GetTrendState(
        AlertTrendSummary trend)
    {
        if (trend.IsWorsening)
        {
            return "worsening";
        }

        if (trend.IsRecurring)
        {
            return "recurring";
        }

        return "single";
    }
}