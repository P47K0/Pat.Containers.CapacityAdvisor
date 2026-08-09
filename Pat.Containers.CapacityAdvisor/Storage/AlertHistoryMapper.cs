using Pat.Containers.CapacityAdvisor.Models.Webhook;
using System.Globalization;
using System.Text.Json;

namespace Pat.Containers.CapacityAdvisor.Storage;

public static class AlertHistoryMapper
{
    public static AlertHistoryEntity MapFromWebhook(
        string clusterName,
        string @namespace,
        string workloadName,
        string signalType,
        AzureMonitorCommonAlert payload)
    {
        var essentials = payload.Data!.Essentials!;

        using var contextDocument =
            ParseAlertContext(payload.Data.AlertContext);

        var context =
            contextDocument.RootElement;

        var effectiveClusterName =
        FirstNonEmpty(
            TryGetString(
                context,
                "properties",
                "clusterName"),

            TryGetString(
                context,
                "labels",
                "cluster"),

            TryGetAksClusterName(essentials),

            clusterName);

        var effectiveNamespace =
        FirstNonEmpty(
            TryGetString(
                context,
                "properties",
                "namespace"),

            TryGetString(
                context,
                "labels",
                "namespace"),

            @namespace);

        var effectiveWorkloadName =
        FirstNonEmpty(
            TryGetString(
                context,
                "properties",
                "workloadName"),

            TryGetString(
                context,
                "labels",
                "workload"),

            TryGetString(
                context,
                "labels",
                "container"),

            workloadName);

        var alertId =
            essentials.AlertId ??
            Guid.NewGuid().ToString("N");

        var partitionKey =
            AlertHistoryKeys.BuildPartitionKey(
                effectiveClusterName,
                effectiveNamespace,
                effectiveWorkloadName);

        var rowKey =
            AlertHistoryKeys.BuildRowKey(
                essentials.FiredDateTime,
                alertId);

        return new AlertHistoryEntity
        {
            PartitionKey = partitionKey,
            RowKey = rowKey,

            ExternalAlertId = alertId,
            RuleName = essentials.AlertRule ?? string.Empty,
            MonitorCondition =
                essentials.MonitorCondition ?? string.Empty,
            Severity = essentials.Severity ?? string.Empty,
            MonitoringService =
                essentials.MonitoringService ?? string.Empty,

            SignalType = signalType,
            ClusterName = effectiveClusterName,
            Namespace = effectiveNamespace,
            WorkloadName = effectiveWorkloadName,

            FiredAtUtc = essentials.FiredDateTime,

            ObservedValue =
                TryGetObservedValue(context),

            RawPayloadJson =
                JsonSerializer.Serialize(payload)
        };
    }

    private static string TryGetAksClusterName(
        AzureMonitorAlertEssentials essentials)
    {
        if (essentials.AlertTargetIDs is not { Length: > 0 })
        {
            return string.Empty;
        }

        foreach (var targetId in essentials.AlertTargetIDs)
        {
            const string marker = "/managedClusters/";

            var index =
                targetId.IndexOf(
                    marker,
                    StringComparison.OrdinalIgnoreCase);

            if (index < 0)
            {
                continue;
            }

            var clusterName =
                targetId[(index + marker.Length)..];

            var slashIndex =
                clusterName.IndexOf('/');

            return slashIndex >= 0
                ? clusterName[..slashIndex]
                : clusterName;
        }

        // ACA target IDs contain /containerApps/.
        // They do not contain an AKS cluster name.
        return string.Empty;
    }

    private static JsonDocument ParseAlertContext(
        JsonElement alertContext)
    {
        // In the supplied JSON, alertContext is an
        // escaped JSON string.
        if (alertContext.ValueKind == JsonValueKind.String)
        {
            var json =
                alertContext.GetString();

            if (!string.IsNullOrWhiteSpace(json))
            {
                return JsonDocument.Parse(json);
            }
        }

        // Also support alertContext as a regular JSON object.
        if (alertContext.ValueKind == JsonValueKind.Object)
        {
            return JsonDocument.Parse(
                alertContext.GetRawText());
        }

        return JsonDocument.Parse("{}");
    }

    private static string TryGetString(
        JsonElement root,
        string sectionName,
        string propertyName)
    {
        if (root.TryGetProperty(
                sectionName,
                out var section) &&
            section.ValueKind == JsonValueKind.Object &&
            section.TryGetProperty(
                propertyName,
                out var property) &&
            property.ValueKind == JsonValueKind.String)
        {
            return property.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static string FirstNonEmpty(
        params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static double? TryGetObservedValue(
    JsonElement context)
    {
        if (context.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        // Prometheus alert value at the root.
        if (TryGetNumber(
                context,
                "expressionValue",
                out var expressionValue))
        {
            return expressionValue;
        }

        // Custom/enriched payload value.
        if (context.TryGetProperty(
                "properties",
                out var properties) &&
            properties.ValueKind == JsonValueKind.Object &&
            TryGetNumber(
                properties,
                "observedValue",
                out var observedValue))
        {
            return observedValue;
        }

        // Metric or Prometheus value inside condition.allOf.
        if (context.TryGetProperty(
                "condition",
                out var condition) &&
            condition.ValueKind == JsonValueKind.Object &&
            condition.TryGetProperty(
                "allOf",
                out var allOf) &&
            allOf.ValueKind == JsonValueKind.Array &&
            allOf.GetArrayLength() > 0)
        {
            var first =
                allOf[0];

            if (TryGetNumber(
                    first,
                    "metricValue",
                    out var metricValue))
            {
                return metricValue;
            }

            if (TryGetNumber(
                    first,
                    "expressionValue",
                    out var conditionExpressionValue))
            {
                return conditionExpressionValue;
            }
        }

        return null;
    }

    private static bool TryGetNumber(
    JsonElement objectElement,
    string propertyName,
    out double value)
    {
        value = 0;

        if (!objectElement.TryGetProperty(
                propertyName,
                out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number &&
            property.TryGetDouble(out value))
        {
            return true;
        }

        if (property.ValueKind == JsonValueKind.String &&
            double.TryParse(
                property.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value))
        {
            return true;
        }

        return false;
    }
}