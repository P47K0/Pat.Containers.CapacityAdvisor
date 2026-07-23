namespace Pat.Containers.CapacityAdvisor.Platform.Aks
{
    using Azure.Core;
    using Azure.Identity;
    using Azure.ResourceManager;
    using Azure.ResourceManager.ContainerService;
    using Microsoft.Extensions.Options;
    using Pat.Containers.CapacityAdvisor.Contracts;
    using Pat.Containers.CapacityAdvisor.Enums;
    using Pat.Containers.CapacityAdvisor.Models;
    using System.Globalization;
    using System.Net.Http.Headers;
    using System.Text.Json;

    public sealed class AksPlatformMetricCollector : IPlatformMetricCollector
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly AksMetricCollectorOptions _options;
        private readonly TokenCredential _credential;
        private readonly ILogger<AksPlatformMetricCollector> _logger;
        private readonly HttpClient _httpClient;

        public AksPlatformMetricCollector(
            IOptions<AksMetricCollectorOptions> options,
            ILogger<AksPlatformMetricCollector> logger,
            HttpClient httpClient)
        {
            _options = options.Value;
            _logger = logger;
            _httpClient = httpClient;
            _credential = new DefaultAzureCredential();
        }

        public async Task<MetricCollectionResult> CollectAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var armClient = new ArmClient(_credential, _options.SubscriptionId);

                var clusterResourceId = ContainerServiceManagedClusterResource.CreateResourceIdentifier(
                    _options.SubscriptionId,
                    _options.ResourceGroup,
                    _options.ClusterName);

                var cluster = armClient.GetContainerServiceManagedClusterResource(clusterResourceId);
                var clusterResponse = await cluster.GetAsync(cancellationToken);
                _ = clusterResponse.Value.Data;

                if (!string.IsNullOrWhiteSpace(_options.PrometheusQueryEndpoint))
                {
                    var prometheusSnapshot = await TryCollectFromPrometheusAsync(
                        clusterResourceId.ToString(),
                        cancellationToken);

                    if (prometheusSnapshot is not null)
                    {
                        return MetricCollectionResult.Ok(prometheusSnapshot);
                    }

                    _logger.LogWarning(
                        "Prometheus query endpoint configured but no usable data returned for AKS cluster {ClusterName}. Falling back to limit-only advice.",
                        _options.ClusterName);
                }

                var fallbackSnapshot = await CollectLimitOnlySnapshotAsync(
                    clusterResourceId.ToString(),
                    cancellationToken);

                return MetricCollectionResult.Ok(fallbackSnapshot);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to collect AKS metrics.");
                return MetricCollectionResult.Failed(ex.Message);
            }
        }

        private async Task<AksPlatformSnapshot?> TryCollectFromPrometheusAsync(
            string clusterResourceId,
            CancellationToken cancellationToken)
        {
            try
            {
                var workloadName = _options.WorkloadName;
                var ns = _options.Namespace;
                var containerFilter = string.IsNullOrWhiteSpace(_options.ContainerName)
                    ? ""
                    : $", container=\"{EscapeLabelValue(_options.ContainerName)}\"";

                var podSelector = $"namespace=\"{EscapeLabelValue(ns)}\", pod=~\"{EscapeRegexValue(workloadName)}-.*\"";
                var window = $"{Math.Max(5, _options.MetricWindowMinutes)}m";

                var cpuUsageQuery =
                    $"sum(rate(container_cpu_usage_seconds_total{{{podSelector}{containerFilter}, image!=\"\", container!=\"POD\"}}[{window}]))";

                var memoryUsageQuery =
                    $"sum(container_memory_working_set_bytes{{{podSelector}{containerFilter}, image!=\"\", container!=\"POD\"}})";

                var cpuRequestQuery =
                    $"sum(kube_pod_container_resource_requests{{{podSelector}{containerFilter}, resource=\"cpu\", unit=\"core\"}})";

                var memoryRequestQuery =
                    $"sum(kube_pod_container_resource_requests{{{podSelector}{containerFilter}, resource=\"memory\", unit=\"byte\"}})";

                var cpuLimitQuery =
                    $"sum(kube_pod_container_resource_limits{{{podSelector}{containerFilter}, resource=\"cpu\", unit=\"core\"}})";

                var memoryLimitQuery =
                    $"sum(kube_pod_container_resource_limits{{{podSelector}{containerFilter}, resource=\"memory\", unit=\"byte\"}})";

                var replicasQuery =
                    $"count(count by (pod) (kube_pod_info{{namespace=\"{EscapeLabelValue(ns)}\", pod=~\"{EscapeRegexValue(workloadName)}-.*\"}}))";

                var nodes = await QueryNodeSnapshotsAsync(cancellationToken);
                if (nodes.Count == 0)
                {
                    return null;
                }

                var cpuUsage = await QueryScalarAsync(cpuUsageQuery, cancellationToken);
                var memoryUsageBytes = await QueryScalarAsync(memoryUsageQuery, cancellationToken);
                var cpuRequest = await QueryScalarAsync(cpuRequestQuery, cancellationToken);
                var memoryRequestBytes = await QueryScalarAsync(memoryRequestQuery, cancellationToken);
                var cpuLimit = await QueryScalarAsync(cpuLimitQuery, cancellationToken);
                var memoryLimitBytes = await QueryScalarAsync(memoryLimitQuery, cancellationToken);
                var replicas = await QueryScalarAsync(replicasQuery, cancellationToken);

                if (cpuRequest is null && memoryRequestBytes is null && cpuLimit is null && memoryLimitBytes is null)
                {
                    return null;
                }

                var snapshot = new AksPlatformSnapshot
                {
                    Platform = "AKS",
                    WorkloadName = workloadName,
                    ResourceId = clusterResourceId,
                    ClusterName = _options.ClusterName,
                    Namespace = ns,
                    AdviceMode = AksAdviceMode.Full,
                    CurrentReplicas = replicas is null ? 1 : Math.Max(1, (int)Math.Round(replicas.Value)),
                    CpuUsageCores = cpuUsage ?? 0,
                    MemoryUsageMb = BytesToMb(memoryUsageBytes),
                    CpuRequestCores = cpuRequest ?? 0,
                    MemoryRequestMb = BytesToMb(memoryRequestBytes),
                    CpuLimitCores = cpuLimit ?? 0,
                    MemoryLimitMb = BytesToMb(memoryLimitBytes),
                    CollectedAtUtc = DateTimeOffset.UtcNow,
                    Nodes = nodes
                };

                snapshot.Placement = EvaluateWithPrometheus(snapshot);
                return snapshot;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Prometheus collection failed for AKS cluster {ClusterName}.", _options.ClusterName);
                return null;
            }
        }

        private async Task<AksPlatformSnapshot> CollectLimitOnlySnapshotAsync(
            string clusterResourceId,
            CancellationToken cancellationToken)
        {
            var cpuUsage = 0d;
            var memoryUsageMb = 0d;

            if (!string.IsNullOrWhiteSpace(_options.PrometheusQueryEndpoint))
            {
                try
                {
                    var workloadName = _options.WorkloadName;
                    var ns = _options.Namespace;
                    var containerFilter = string.IsNullOrWhiteSpace(_options.ContainerName)
                        ? ""
                        : $", container=\"{EscapeLabelValue(_options.ContainerName)}\"";
                    var podSelector = $"namespace=\"{EscapeLabelValue(ns)}\", pod=~\"{EscapeRegexValue(workloadName)}-.*\"";
                    var window = $"{Math.Max(5, _options.MetricWindowMinutes)}m";

                    var cpuUsageQuery =
                        $"sum(rate(container_cpu_usage_seconds_total{{{podSelector}{containerFilter}, image!=\"\", container!=\"POD\"}}[{window}]))";

                    var memoryUsageQuery =
                        $"sum(container_memory_working_set_bytes{{{podSelector}{containerFilter}, image!=\"\", container!=\"POD\"}})";

                    cpuUsage = (await QueryScalarAsync(cpuUsageQuery, cancellationToken)) ?? 0;
                    memoryUsageMb = BytesToMb(await QueryScalarAsync(memoryUsageQuery, cancellationToken));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Unable to collect usage metrics for limit-only AKS advice.");
                }
            }

            var snapshot = new AksPlatformSnapshot
            {
                Platform = "AKS",
                WorkloadName = _options.WorkloadName,
                ResourceId = clusterResourceId,
                ClusterName = _options.ClusterName,
                Namespace = _options.Namespace,
                AdviceMode = AksAdviceMode.LimitOnly,
                CurrentReplicas = 1,
                CpuUsageCores = cpuUsage,
                MemoryUsageMb = memoryUsageMb,
                CpuRequestCores = 0,
                MemoryRequestMb = 0,
                CpuLimitCores = _options.CurrentCpuLimitCores,
                MemoryLimitMb = _options.CurrentMemoryLimitMb,
                CollectedAtUtc = DateTimeOffset.UtcNow,
                Nodes = [],
                Placement = EvaluateWithoutPrometheus(
                    cpuUsage,
                    memoryUsageMb,
                    _options.CurrentCpuLimitCores,
                    _options.CurrentMemoryLimitMb,
                    _options.LimitPressureThreshold)
            };

            return snapshot;
        }

        private async Task<List<AksNodeSnapshot>> QueryNodeSnapshotsAsync(CancellationToken cancellationToken)
        {
            var allocCpuQuery =
                "sum by (node) (kube_node_status_allocatable{resource=\"cpu\", unit=\"core\"})";

            var allocMemQuery =
                "sum by (node) (kube_node_status_allocatable{resource=\"memory\", unit=\"byte\"})";

            var reqCpuQuery =
                "sum by (node) (kube_pod_container_resource_requests{resource=\"cpu\", unit=\"core\"} * on(namespace,pod) group_left(node) kube_pod_info)";

            var reqMemQuery =
                "sum by (node) (kube_pod_container_resource_requests{resource=\"memory\", unit=\"byte\"} * on(namespace,pod) group_left(node) kube_pod_info)";

            var usageCpuQuery =
                "sum by (node) (rate(container_cpu_usage_seconds_total{image!=\"\", container!=\"POD\"}[5m]) * on(namespace,pod) group_left(node) kube_pod_info)";

            var usageMemQuery =
                "sum by (node) (container_memory_working_set_bytes{image!=\"\", container!=\"POD\"} * on(namespace,pod) group_left(node) kube_pod_info)";

            var allocCpu = await QueryVectorAsync(allocCpuQuery, cancellationToken);
            var allocMem = await QueryVectorAsync(allocMemQuery, cancellationToken);
            var reqCpu = await QueryVectorAsync(reqCpuQuery, cancellationToken);
            var reqMem = await QueryVectorAsync(reqMemQuery, cancellationToken);
            var usageCpu = await QueryVectorAsync(usageCpuQuery, cancellationToken);
            var usageMem = await QueryVectorAsync(usageMemQuery, cancellationToken);

            var nodes = new Dictionary<string, AksNodeSnapshot>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in allocCpu)
            {
                if (!TryGetNodeName(item, out var nodeName))
                {
                    continue;
                }

                UpsertNode(nodes, nodeName, n => n.CpuAllocatableCores = item.Value.Value);
            }

            foreach (var item in allocMem)
            {
                if (!TryGetNodeName(item, out var nodeName))
                {
                    continue;
                }

                UpsertNode(nodes, nodeName, n => n.MemoryAllocatableMb = BytesToMb(item.Value.Value));
            }

            foreach (var item in reqCpu)
            {
                if (!TryGetNodeName(item, out var nodeName))
                {
                    continue;
                }

                UpsertNode(nodes, nodeName, n => n.CpuRequestedCores = item.Value.Value);
            }

            foreach (var item in reqMem)
            {
                if (!TryGetNodeName(item, out var nodeName))
                {
                    continue;
                }

                UpsertNode(nodes, nodeName, n => n.MemoryRequestedMb = BytesToMb(item.Value.Value));
            }

            foreach (var item in usageCpu)
            {
                if (!TryGetNodeName(item, out var nodeName))
                {
                    continue;
                }

                UpsertNode(nodes, nodeName, n => n.CpuUsageCores = item.Value.Value);
            }

            foreach (var item in usageMem)
            {
                if (!TryGetNodeName(item, out var nodeName))
                {
                    continue;
                }

                UpsertNode(nodes, nodeName, n => n.MemoryUsageMb = BytesToMb(item.Value.Value));
            }

            return nodes.Values.OrderBy(n => n.NodeName).ToList();
        }

        private AksPlacementAdvice EvaluateWithPrometheus(AksPlatformSnapshot snapshot)
        {
            var cpuLimitPressure = snapshot.CpuLimitCores > 0 &&
                                   snapshot.CpuUsageCores / snapshot.CpuLimitCores >= _options.LimitPressureThreshold;

            var memoryLimitPressure = snapshot.MemoryLimitMb > 0 &&
                                      snapshot.MemoryUsageMb / snapshot.MemoryLimitMb >= _options.LimitPressureThreshold;

            var candidate = snapshot.Nodes
                .Where(n => snapshot.CpuRequestCores <= n.FreeCpuByRequests &&
                            snapshot.MemoryRequestMb <= n.FreeMemoryByRequestsMb)
                .OrderByDescending(n => n.FreeCpuByRequests - snapshot.CpuRequestCores)
                .ThenByDescending(n => n.FreeMemoryByRequestsMb - snapshot.MemoryRequestMb)
                .FirstOrDefault();

            if (candidate is null)
            {
                return new AksPlacementAdvice
                {
                    Mode = AksAdviceMode.Full,
                    CanAssessNodeFit = true,
                    CanAssessNeedForNewNode = true,
                    FitsExistingNode = false,
                    NeedsNewNode = true,
                    RecommendedNode = null,
                    ShouldIncreaseCpuLimit = cpuLimitPressure,
                    ShouldIncreaseMemoryLimit = memoryLimitPressure,
                    RiskLevel = "High",
                    Reason = "No AKS node has enough free allocatable CPU and memory for the current pod requests."
                };
            }

            var projectedCpuRatio = candidate.CpuAllocatableCores <= 0
                ? 1
                : (candidate.CpuRequestedCores + snapshot.CpuRequestCores) / candidate.CpuAllocatableCores;

            var projectedMemoryRatio = candidate.MemoryAllocatableMb <= 0
                ? 1
                : (candidate.MemoryRequestedMb + snapshot.MemoryRequestMb) / candidate.MemoryAllocatableMb;

            var riskLevel = projectedCpuRatio >= 0.9 || projectedMemoryRatio >= 0.9
                ? "High"
                : projectedCpuRatio >= 0.75 || projectedMemoryRatio >= 0.75
                    ? "Medium"
                    : "Low";

            return new AksPlacementAdvice
            {
                Mode = AksAdviceMode.Full,
                CanAssessNodeFit = true,
                CanAssessNeedForNewNode = true,
                FitsExistingNode = true,
                NeedsNewNode = false,
                RecommendedNode = candidate.NodeName,
                ShouldIncreaseCpuLimit = cpuLimitPressure,
                ShouldIncreaseMemoryLimit = memoryLimitPressure,
                RiskLevel = riskLevel,
                Reason = riskLevel == "Low"
                    ? "The workload requests fit on an existing AKS node."
                    : "The workload requests fit on an existing AKS node, but remaining headroom is limited."
            };
        }

        private static AksPlacementAdvice EvaluateWithoutPrometheus(
            double cpuUsageCores,
            double memoryUsageMb,
            double cpuLimitCores,
            double memoryLimitMb,
            double threshold)
        {
            var shouldIncreaseCpu = cpuLimitCores > 0 &&
                                    cpuUsageCores / cpuLimitCores >= threshold;

            var shouldIncreaseMemory = memoryLimitMb > 0 &&
                                       memoryUsageMb / memoryLimitMb >= threshold;

            return new AksPlacementAdvice
            {
                Mode = AksAdviceMode.LimitOnly,
                CanAssessNodeFit = false,
                CanAssessNeedForNewNode = false,
                FitsExistingNode = false,
                NeedsNewNode = false,
                RecommendedNode = null,
                ShouldIncreaseCpuLimit = shouldIncreaseCpu,
                ShouldIncreaseMemoryLimit = shouldIncreaseMemory,
                RiskLevel = shouldIncreaseCpu || shouldIncreaseMemory ? "Medium" : "Low",
                Reason = "Managed Prometheus is unavailable. Only limit pressure advice can be returned."
            };
        }

        private async Task<double?> QueryScalarAsync(string promQl, CancellationToken cancellationToken)
        {
            var result = await QueryAsync(promQl, cancellationToken);
            if (result?.Data?.Result is null || result.Data.Result.Count == 0)
            {
                return null;
            }

            return result.Data.Result[0].Value;
        }

        private async Task<List<PrometheusVectorResult>> QueryVectorAsync(string promQl, CancellationToken cancellationToken)
        {
            var result = await QueryAsync(promQl, cancellationToken);
            return result?.Data?.Result ?? [];
        }

        private async Task<PrometheusQueryResponse?> QueryAsync(string promQl, CancellationToken cancellationToken)
        {
            var endpoint = _options.PrometheusQueryEndpoint?.TrimEnd('/');
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                return null;
            }

            var token = await _credential.GetTokenAsync(
                new TokenRequestContext(["https://prometheus.monitor.azure.com/.default"]),
                cancellationToken);

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{endpoint}/api/v1/query");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["query"] = promQl
            });

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException(
                    $"Prometheus query failed with status {(int)response.StatusCode}: {body}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var result = await JsonSerializer.DeserializeAsync<PrometheusQueryResponse>(stream, JsonOptions, cancellationToken);

            if (!string.Equals(result?.Status, "success", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return result;
        }

        private static void UpsertNode(
            IDictionary<string, AksNodeSnapshot> nodes,
            string nodeName,
            Action<AksNodeSnapshot> update)
        {
            if (!nodes.TryGetValue(nodeName, out var node))
            {
                node = new AksNodeSnapshot { NodeName = nodeName };
                nodes[nodeName] = node;
            }

            update(node);
        }

        private static bool TryGetNodeName(PrometheusVectorResult item, out string nodeName)
        {
            if (item.Metric.TryGetValue("node", out var node) && !string.IsNullOrWhiteSpace(node))
            {
                nodeName = node;
                return true;
            }

            if (item.Metric.TryGetValue("instance", out var instance) && !string.IsNullOrWhiteSpace(instance))
            {
                nodeName = instance;
                return true;
            }

            nodeName = string.Empty;
            return false;
        }

        private static string EscapeLabelValue(string value) =>
            value.Replace("\\", "\\\\", StringComparison.Ordinal)
                 .Replace("\"", "\\\"", StringComparison.Ordinal);

        private static string EscapeRegexValue(string value) =>
            EscapeLabelValue(value).Replace(".", "\\.", StringComparison.Ordinal);

        private static double BytesToMb(double? bytes) =>
            bytes is null ? 0 : bytes.Value / 1024d / 1024d;

        private sealed class PrometheusQueryResponse
        {
            public string? Status { get; set; }
            public PrometheusQueryData? Data { get; set; }
        }

        private sealed class PrometheusQueryData
        {
            public string? ResultType { get; set; }
            public List<PrometheusVectorResult> Result { get; set; } = [];
        }

        private sealed class PrometheusVectorResult
        {
            public Dictionary<string, string> Metric { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            public JsonElement ValueRaw { get; set; }

            public double? Value
            {
                get
                {
                    if (ValueRaw.ValueKind != JsonValueKind.Array || ValueRaw.GetArrayLength() < 2)
                    {
                        return null;
                    }

                    var raw = ValueRaw[1].GetString();
                    return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                        ? parsed
                        : null;
                }
            }
        }
    }

}
