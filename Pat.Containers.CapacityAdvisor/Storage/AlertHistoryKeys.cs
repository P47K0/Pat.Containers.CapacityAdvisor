using System.Security.Cryptography;
using System.Text;

namespace Pat.Containers.CapacityAdvisor.Storage;

public static class AlertHistoryKeys
{
    public static string BuildPartitionKey(
        string clusterName,
        string @namespace,
        string workloadName)
    {
        return
            $"{clusterName}|{@namespace}|{workloadName}";
    }

    public static string BuildRowKey(
        DateTimeOffset firedAtUtc,
        string alertId)
    {
        var alertIdHash = SHA256.HashData(
            Encoding.UTF8.GetBytes(alertId));

        var safeAlertId =
            Convert.ToHexString(alertIdHash)
                .ToLowerInvariant();

        return
            $"{firedAtUtc.UtcTicks:D20}_{safeAlertId}";
    }
}