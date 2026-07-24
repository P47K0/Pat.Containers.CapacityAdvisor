namespace Pat.Containers.CapacityAdvisor.Platform.Aks
{
    using Azure.Core;
    using Azure.Identity;
    using Azure.ResourceManager;
    using Azure.ResourceManager.ContainerService;
    using k8s;
    using k8s.Models;
    using Microsoft.Extensions.Options;
    using Pat.Containers.CapacityAdvisor.Contracts;
    using Pat.Containers.CapacityAdvisor.Enums;
    using Pat.Containers.CapacityAdvisor.Models;
    using System.Globalization;
    using System.Net.Http.Headers;
    using System.Text;
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
                        _logger.LogInformation(
                            "Fetched AKS metrics via Prometheus for cluster {ClusterName}, namespace {Namespace}, workload {WorkloadName}. Replicas={Replicas}, CpuUsageCores={CpuUsageCores}, MemoryUsageMb={MemoryUsageMb}, CpuRequestCores={CpuRequestCores}, MemoryRequestMb={MemoryRequestMb}, CpuLimitCores={CpuLimitCores}, MemoryLimitMb={MemoryLimitMb}",
                            prometheusSnapshot.ClusterName,
                            prometheusSnapshot.Namespace,
                            prometheusSnapshot.WorkloadName,
                            prometheusSnapshot.CurrentReplicas,
                            prometheusSnapshot.CpuUsageCores,
                            prometheusSnapshot.MemoryUsageMb,
                            prometheusSnapshot.CpuRequestCores,
                            prometheusSnapshot.MemoryRequestMb,
                            prometheusSnapshot.CpuLimitCores,
                            prometheusSnapshot.MemoryLimitMb);

                        return MetricCollectionResult.Ok(prometheusSnapshot);
                    }

                    _logger.LogWarning(
                        "Prometheus query endpoint configured but no usable data returned for AKS cluster {ClusterName}. Falling back to Kubernetes Metrics API.",
                        _options.ClusterName);
                }

                var metricsApiSnapshot = await TryCollectFromMetricsApiAsync(
                    clusterResourceId.ToString(),
                    cancellationToken);

                if (metricsApiSnapshot is not null)
                {
                    _logger.LogInformation(
                        "Fetched AKS pod usage via Kubernetes Metrics API for cluster {ClusterName}, namespace {Namespace}, workload {WorkloadName}. Replicas={Replicas}, CpuUsageCores={CpuUsageCores}, MemoryUsageMb={MemoryUsageMb}, CpuRequestCores={CpuRequestCores}, MemoryRequestMb={MemoryRequestMb}, CpuLimitCores={CpuLimitCores}, MemoryLimitMb={MemoryLimitMb}, NodeCount={NodeCount}",
                        metricsApiSnapshot.ClusterName,
                        metricsApiSnapshot.Namespace,
                        metricsApiSnapshot.WorkloadName,
                        metricsApiSnapshot.CurrentReplicas,
                        metricsApiSnapshot.CpuUsageCores,
                        metricsApiSnapshot.MemoryUsageMb,
                        metricsApiSnapshot.CpuRequestCores,
                        metricsApiSnapshot.MemoryRequestMb,
                        metricsApiSnapshot.CpuLimitCores,
                        metricsApiSnapshot.MemoryLimitMb,
                        metricsApiSnapshot.Nodes.Count);

                    return MetricCollectionResult.Ok(metricsApiSnapshot);
                }

                var specSnapshot = await TryCollectFromKubernetesSpecAsync(
                    clusterResourceId.ToString(),
                    cancellationToken);

                if (specSnapshot is not null)
                {
                    _logger.LogInformation(
                        "Fetched AKS workload spec via Kubernetes API for cluster {ClusterName}, namespace {Namespace}, workload {WorkloadName}. Replicas={Replicas}, CpuRequestCores={CpuRequestCores}, MemoryRequestMb={MemoryRequestMb}, CpuLimitCores={CpuLimitCores}, MemoryLimitMb={MemoryLimitMb}, NodeCount={NodeCount}",
                        specSnapshot.ClusterName,
                        specSnapshot.Namespace,
                        specSnapshot.WorkloadName,
                        specSnapshot.CurrentReplicas,
                        specSnapshot.CpuRequestCores,
                        specSnapshot.MemoryRequestMb,
                        specSnapshot.CpuLimitCores,
                        specSnapshot.MemoryLimitMb,
                        specSnapshot.Nodes.Count);

                    return MetricCollectionResult.Ok(specSnapshot);
                }

                _logger.LogWarning(
                    "Kubernetes workload spec fallback returned no usable data for AKS cluster {ClusterName}. Falling back to limit-only advice.",
                    _options.ClusterName);

                var fallbackSnapshot = await CollectLimitOnlySnapshotAsync(
                    clusterResourceId.ToString(),
                    cancellationToken);

                _logger.LogInformation(
                    "Fetched limit-only AKS snapshot for cluster {ClusterName}, namespace {Namespace}, workload {WorkloadName}. CpuUsageCores={CpuUsageCores}, MemoryUsageMb={MemoryUsageMb}, CpuLimitCores={CpuLimitCores}, MemoryLimitMb={MemoryLimitMb}",
                    fallbackSnapshot.ClusterName,
                    fallbackSnapshot.Namespace,
                    fallbackSnapshot.WorkloadName,
                    fallbackSnapshot.CpuUsageCores,
                    fallbackSnapshot.MemoryUsageMb,
                    fallbackSnapshot.CpuLimitCores,
                    fallbackSnapshot.MemoryLimitMb);

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

                var clusterCapacity = BuildClusterCapacity(nodes);

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
                    Nodes = nodes,
                    ClusterCapacity = clusterCapacity
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

        private async Task<AksPlatformSnapshot?> TryCollectFromMetricsApiAsync(
            string clusterResourceId,
            CancellationToken cancellationToken)
        {
            try
            {
                var config = KubernetesClientConfiguration.InClusterConfig();
                using var client = new Kubernetes(config);

                var workload = await ReadWorkloadSpecAsync(client, cancellationToken);
                if (workload is null || workload.Template?.Spec?.Containers is null || workload.Template.Spec.Containers.Count == 0)
                {
                    return null;
                }

                var selector = BuildLabelSelector(workload.Template.Metadata?.Labels);
                if (string.IsNullOrWhiteSpace(selector))
                {
                    selector = $"app={_options.WorkloadName}";
                }

                var podList = await client.CoreV1.ListNamespacedPodAsync(
                    _options.Namespace,
                    labelSelector: selector,
                    cancellationToken: cancellationToken);

                var matchingPods = podList.Items
                    .Where(p => IsRunningWorkloadPod(p, _options.WorkloadName))
                    .ToList();

                if (matchingPods.Count == 0)
                {
                    return null;
                }

                var podMetricsList = await client.GetKubernetesPodsMetricsByNamespaceAsync(
                    _options.Namespace);

                var podMetricsByName = podMetricsList.Items
                    .Where(m => m.Metadata?.Name is not null)
                    .ToDictionary(m => m.Metadata!.Name!, StringComparer.OrdinalIgnoreCase);

                var cpuUsageCores = 0d;
                var memoryUsageMb = 0d;

                foreach (var pod in matchingPods)
                {
                    var podName = pod.Metadata?.Name;
                    if (string.IsNullOrWhiteSpace(podName) || !podMetricsByName.TryGetValue(podName, out var podMetrics))
                    {
                        continue;
                    }

                    foreach (var containerMetric in podMetrics.Containers ?? [])
                    {
                        if (!string.IsNullOrWhiteSpace(_options.ContainerName) &&
                            !string.Equals(containerMetric.Name, _options.ContainerName, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        cpuUsageCores += ParseCpuCores(containerMetric.Usage, "cpu");
                        memoryUsageMb += ParseMemoryMb(containerMetric.Usage, "memory");
                    }
                }

                var containers = string.IsNullOrWhiteSpace(_options.ContainerName)
                    ? workload.Template.Spec.Containers
                    : workload.Template.Spec.Containers
                        .Where(c => string.Equals(c.Name, _options.ContainerName, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                if (containers.Count == 0)
                {
                    return null;
                }

                var cpuRequestCores = containers.Sum(c => ParseCpuCores(c.Resources?.Requests, "cpu"));
                var memoryRequestMb = containers.Sum(c => ParseMemoryMb(c.Resources?.Requests, "memory"));
                var cpuLimitCores = containers.Sum(c => ParseCpuCores(c.Resources?.Limits, "cpu"));
                var memoryLimitMb = containers.Sum(c => ParseMemoryMb(c.Resources?.Limits, "memory"));
                var nodes = await QueryNodeSnapshotsFromKubernetesAsync(client, cancellationToken);

                var clusterCapacity = BuildClusterCapacity(nodes);

                var snapshot = new AksPlatformSnapshot
                {
                    Platform = "AKS",
                    WorkloadName = _options.WorkloadName,
                    ResourceId = clusterResourceId,
                    ClusterName = _options.ClusterName,
                    Namespace = _options.Namespace,
                    AdviceMode = AksAdviceMode.Full,
                    CurrentReplicas = Math.Max(1, matchingPods.Count),
                    CpuUsageCores = cpuUsageCores,
                    MemoryUsageMb = memoryUsageMb,
                    CpuRequestCores = cpuRequestCores,
                    MemoryRequestMb = memoryRequestMb,
                    CpuLimitCores = cpuLimitCores,
                    MemoryLimitMb = memoryLimitMb,
                    CollectedAtUtc = DateTimeOffset.UtcNow,
                    Nodes = nodes,
                    ClusterCapacity = clusterCapacity
                };

                snapshot.Placement = EvaluateWithPrometheus(snapshot);
                return snapshot;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Kubernetes Metrics API fallback failed for AKS cluster {ClusterName}.", _options.ClusterName);
                return null;
            }
        }

        private async Task<AksPlatformSnapshot?> TryCollectFromKubernetesSpecAsync(
            string clusterResourceId,
            CancellationToken cancellationToken)
        {
            try
            {
                var config = KubernetesClientConfiguration.InClusterConfig();
                using var client = new Kubernetes(config);

                var workload = await ReadWorkloadSpecAsync(client, cancellationToken);
                if (workload is null || workload.Template?.Spec?.Containers is null || workload.Template.Spec.Containers.Count == 0)
                {
                    return null;
                }

                var containers = string.IsNullOrWhiteSpace(_options.ContainerName)
                    ? workload.Template.Spec.Containers
                    : workload.Template.Spec.Containers
                        .Where(c => string.Equals(c.Name, _options.ContainerName, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                if (containers.Count == 0)
                {
                    return null;
                }

                var cpuRequestCores = containers.Sum(c => ParseCpuCores(c.Resources?.Requests, "cpu"));
                var memoryRequestMb = containers.Sum(c => ParseMemoryMb(c.Resources?.Requests, "memory"));
                var cpuLimitCores = containers.Sum(c => ParseCpuCores(c.Resources?.Limits, "cpu"));
                var memoryLimitMb = containers.Sum(c => ParseMemoryMb(c.Resources?.Limits, "memory"));

                var replicas = workload.Replicas > 0 ? workload.Replicas : 1;
                var nodes = await QueryNodeSnapshotsFromKubernetesAsync(client, cancellationToken);

                var clusterCapacity = BuildClusterCapacity(nodes);

                var snapshot = new AksPlatformSnapshot
                {
                    Platform = "AKS",
                    WorkloadName = _options.WorkloadName,
                    ResourceId = clusterResourceId,
                    ClusterName = _options.ClusterName,
                    Namespace = _options.Namespace,
                    AdviceMode = AksAdviceMode.LimitOnly,
                    CurrentReplicas = replicas,
                    CpuUsageCores = 0,
                    MemoryUsageMb = 0,
                    CpuRequestCores = cpuRequestCores,
                    MemoryRequestMb = memoryRequestMb,
                    CpuLimitCores = cpuLimitCores,
                    MemoryLimitMb = memoryLimitMb,
                    CollectedAtUtc = DateTimeOffset.UtcNow,
                    Nodes = nodes,
                    ClusterCapacity = clusterCapacity,
                    Placement = EvaluateSpecOnly(
                        cpuRequestCores,
                        memoryRequestMb,
                        cpuLimitCores,
                        memoryLimitMb,
                        nodes)
                };

                return snapshot;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Kubernetes API workload spec fallback failed for AKS cluster {ClusterName}.", _options.ClusterName);
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

        private async Task<List<AksNodeSnapshot>> QueryNodeSnapshotsFromKubernetesAsync(
    Kubernetes client,
    CancellationToken cancellationToken)
        {
            var nodeList = await client.CoreV1.ListNodeAsync(cancellationToken: cancellationToken);
            var podList = await client.CoreV1.ListPodForAllNamespacesAsync(cancellationToken: cancellationToken);
            var nodeMetrics = await GetNodeMetricsAsync(client, cancellationToken);

            var nodes = new Dictionary<string, AksNodeSnapshot>(StringComparer.OrdinalIgnoreCase);

            foreach (var node in nodeList.Items)
            {
                var nodeName = node.Metadata?.Name;
                if (string.IsNullOrWhiteSpace(nodeName))
                {
                    continue;
                }

                var snapshot = new AksNodeSnapshot
                {
                    NodeName = nodeName,
                    CpuAllocatableCores = ParseCpuCores(node.Status?.Allocatable, "cpu"),
                    MemoryAllocatableMb = ParseMemoryMb(node.Status?.Allocatable, "memory"),
                    Ready = IsNodeReady(node),
                    Schedulable = !node.Spec?.Unschedulable.GetValueOrDefault() ?? true
                };

                if (nodeMetrics.TryGetValue(nodeName, out var metric))
                {
                    snapshot.CpuUsageCores = ParseCpuQuantityToCores(metric.Usage.TryGetValue("cpu", out var cpu) ? cpu : null);
                    snapshot.MemoryUsageMb = BytesToMb(ParseKubernetesMemoryBytes(metric.Usage.TryGetValue("memory", out var memory) ? memory : null));
                }

                nodes[nodeName] = snapshot;
            }

            foreach (var pod in podList.Items)
            {
                var nodeName = pod.Spec?.NodeName;
                if (string.IsNullOrWhiteSpace(nodeName) || !nodes.TryGetValue(nodeName, out var node))
                {
                    continue;
                }

                if (ShouldSkipPodForCapacityAccounting(pod))
                {
                    continue;
                }

                foreach (var container in pod.Spec?.Containers ?? [])
                {
                    node.CpuRequestedCores += ParseCpuCores(container.Resources?.Requests, "cpu");
                    node.MemoryRequestedMb += ParseMemoryMb(container.Resources?.Requests, "memory");
                    node.CpuLimitsCores += ParseCpuCores(container.Resources?.Limits, "cpu");
                    node.MemoryLimitsMb += ParseMemoryMb(container.Resources?.Limits, "memory");
                }

                foreach (var initContainer in pod.Spec?.InitContainers ?? [])
                {
                    node.CpuRequestedCores += ParseCpuCores(initContainer.Resources?.Requests, "cpu");
                    node.MemoryRequestedMb += ParseMemoryMb(initContainer.Resources?.Requests, "memory");
                    node.CpuLimitsCores += ParseCpuCores(initContainer.Resources?.Limits, "cpu");
                    node.MemoryLimitsMb += ParseMemoryMb(initContainer.Resources?.Limits, "memory");
                }
            }

            return nodes.Values
                .Where(n => n.Ready && n.Schedulable)
                .OrderBy(n => n.NodeName)
                .ToList();
        }

        private static bool IsNodeReady(V1Node node)
        {
            var readyCondition = node.Status?.Conditions?
                .FirstOrDefault(c => string.Equals(c.Type, "Ready", StringComparison.OrdinalIgnoreCase));

            return string.Equals(readyCondition?.Status, "True", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ShouldSkipPodForCapacityAccounting(V1Pod pod)
        {
            var phase = pod.Status?.Phase ?? string.Empty;

            if (string.Equals(phase, "Succeeded", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(phase, "Failed", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private static AksClusterCapacitySnapshot BuildClusterCapacity(IReadOnlyCollection<AksNodeSnapshot> nodes)
        {
            var readyNodes = nodes.Where(n => n.Ready && n.Schedulable).ToList();

            return new AksClusterCapacitySnapshot
            {
                TotalNodeCount = nodes.Count,
                ReadySchedulableNodeCount = readyNodes.Count,
                TotalCpuAllocatableCores = readyNodes.Sum(n => n.CpuAllocatableCores),
                TotalMemoryAllocatableMb = readyNodes.Sum(n => n.MemoryAllocatableMb),
                TotalCpuUsageCores = readyNodes.Sum(n => n.CpuUsageCores),
                TotalMemoryUsageMb = readyNodes.Sum(n => n.MemoryUsageMb),
                TotalCpuRequestedCores = readyNodes.Sum(n => n.CpuRequestedCores),
                TotalMemoryRequestedMb = readyNodes.Sum(n => n.MemoryRequestedMb),
                TotalCpuLimitsCores = readyNodes.Sum(n => n.CpuLimitsCores),
                TotalMemoryLimitsMb = readyNodes.Sum(n => n.MemoryLimitsMb)
            };
        }

        private async Task<Dictionary<string, KubernetesNodeMetricItem>> GetNodeMetricsAsync(
    Kubernetes client,
    CancellationToken cancellationToken)
        {
            var raw = await client.CustomObjects.ListClusterCustomObjectAsync(
                group: "metrics.k8s.io",
                version: "v1beta1",
                plural: "nodes",
                cancellationToken: cancellationToken);

            var json = JsonSerializer.Serialize(raw, JsonOptions);
            var metrics = JsonSerializer.Deserialize<KubernetesNodeMetricList>(json, JsonOptions);

            return metrics?.Items?
                .Where(x => !string.IsNullOrWhiteSpace(x.Metadata?.Name))
                .ToDictionary(x => x.Metadata!.Name!, StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, KubernetesNodeMetricItem>(StringComparer.OrdinalIgnoreCase);
        }

        private async Task<KubernetesWorkloadSpec?> ReadWorkloadSpecAsync(Kubernetes client, CancellationToken cancellationToken)
        {
            var ns = _options.Namespace;
            var workloadName = _options.WorkloadName;
            var workloadKind = _options.WorkloadKind?.Trim();

            if (string.Equals(workloadKind, "StatefulSet", StringComparison.OrdinalIgnoreCase))
            {
                var statefulSet = await client.AppsV1.ReadNamespacedStatefulSetAsync(workloadName, ns, cancellationToken: cancellationToken);
                return new KubernetesWorkloadSpec
                {
                    Replicas = statefulSet.Spec?.Replicas ?? 1,
                    Template = statefulSet.Spec?.Template
                };
            }

            var deployment = await client.AppsV1.ReadNamespacedDeploymentAsync(workloadName, ns, cancellationToken: cancellationToken);
            return new KubernetesWorkloadSpec
            {
                Replicas = deployment.Spec?.Replicas ?? 1,
                Template = deployment.Spec?.Template
            };
        }

        private AksPlacementAdvice EvaluateWithPrometheus(AksPlatformSnapshot snapshot)
        {
            var cpuLimitPressure = snapshot.CpuLimitCores > 0 &&
                                   snapshot.CpuUsageCores / snapshot.CpuLimitCores >= _options.LimitPressureThreshold;

            var memoryLimitPressure = snapshot.MemoryLimitMb > 0 &&
                                      snapshot.MemoryUsageMb / snapshot.MemoryLimitMb >= _options.LimitPressureThreshold;

            var candidate = snapshot.Nodes
                .Where(n => n.Ready &&
                            n.Schedulable &&
                            snapshot.CpuRequestCores <= n.FreeCpuByRequests &&
                            snapshot.MemoryRequestMb <= n.FreeMemoryByRequestsMb)
                .OrderByDescending(n => n.FreeCpuByRequests - snapshot.CpuRequestCores)
                .ThenByDescending(n => n.FreeMemoryByRequestsMb - snapshot.MemoryRequestMb)
                .FirstOrDefault();

            var cluster = snapshot.ClusterCapacity;

            if (candidate is null)
            {
                var noFitReason = cluster is null
                    ? "No ready schedulable AKS node has enough free CPU and memory by requests for the current workload."
                    : string.Format(
                        CultureInfo.InvariantCulture,
                        "No ready schedulable AKS node has enough free CPU and memory by requests for the current workload. " +
                        "Cluster free capacity by requests is {0:F2} CPU cores and {1:F0} MB memory. " +
                        "Cluster request saturation is {2:F1}% CPU and {3:F1}% memory. " +
                        "Cluster limit overcommit is {4:F1}% CPU and {5:F1}% memory.",
                        cluster.TotalFreeCpuByRequestsCores,
                        cluster.TotalFreeMemoryByRequestsMb,
                        cluster.CpuRequestSaturationPercent,
                        cluster.MemoryRequestSaturationPercent,
                        cluster.CpuLimitOvercommitPercent,
                        cluster.MemoryLimitOvercommitPercent);

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
                    Reason = noFitReason
                };
            }

            var projectedCpuRequestPercent =
                candidate.ProjectedCpuRequestSaturationPercent(snapshot.CpuRequestCores);

            var projectedMemoryRequestPercent =
                candidate.ProjectedMemoryRequestSaturationPercent(snapshot.MemoryRequestMb);

            var projectedCpuLivePercent =
                candidate.ProjectedCpuLiveUsagePercent(snapshot.CpuUsageCores);

            var projectedMemoryLivePercent =
                candidate.ProjectedMemoryLiveUsagePercent(snapshot.MemoryUsageMb);

            var riskLevel =
                projectedCpuRequestPercent >= 90 ||
                projectedMemoryRequestPercent >= 90 ||
                projectedCpuLivePercent >= 90 ||
                projectedMemoryLivePercent >= 90 ||
                candidate.MemoryLimitOvercommitPercent >= 130
                    ? "High"
                    : projectedCpuRequestPercent >= 75 ||
                      projectedMemoryRequestPercent >= 75 ||
                      projectedCpuLivePercent >= 75 ||
                      projectedMemoryLivePercent >= 75 ||
                      candidate.MemoryLimitOvercommitPercent >= 100 ||
                      candidate.CpuLimitOvercommitPercent >= 150
                        ? "Medium"
                        : "Low";

            var shouldSuggestNewNode =
                riskLevel == "High" &&
                cluster is not null &&
                cluster.ReadySchedulableNodeCount > 0;

            var reasonBuilder = new StringBuilder();
            reasonBuilder.AppendFormat(
                CultureInfo.InvariantCulture,
                "The workload requests fit on node {0}. " +
                "Free capacity by requests before placement is {1:F2} CPU cores and {2:F0} MB memory. " +
                "Projected request saturation after placement is {3:F1}% CPU and {4:F1}% memory. " +
                "Current live usage on that node is {5:F1}% CPU and {6:F1}% memory, and projected live usage after placement is {7:F1}% CPU and {8:F1}% memory. " +
                "Current limit overcommit on that node is {9:F1}% CPU and {10:F1}% memory.",
                candidate.NodeName,
                candidate.FreeCpuByRequests,
                candidate.FreeMemoryByRequestsMb,
                projectedCpuRequestPercent,
                projectedMemoryRequestPercent,
                candidate.CpuLiveUsagePercent,
                candidate.MemoryLiveUsagePercent,
                projectedCpuLivePercent,
                projectedMemoryLivePercent,
                candidate.CpuLimitOvercommitPercent,
                candidate.MemoryLimitOvercommitPercent);

            if (cluster is not null)
            {
                reasonBuilder.AppendFormat(
                    CultureInfo.InvariantCulture,
                    " Cluster request saturation is {0:F1}% CPU and {1:F1}% memory. " +
                    "Cluster limit overcommit is {2:F1}% CPU and {3:F1}% memory.",
                    cluster.CpuRequestSaturationPercent,
                    cluster.MemoryRequestSaturationPercent,
                    cluster.CpuLimitOvercommitPercent,
                    cluster.MemoryLimitOvercommitPercent);
            }

            if (shouldSuggestNewNode)
            {
                reasonBuilder.Append(" The workload can be scheduled, but post-placement risk is high enough that adding a node is the safer operational choice.");
            }
            else if (riskLevel == "Medium")
            {
                reasonBuilder.Append(" The workload fits, but remaining headroom is getting tighter.");
            }
            else
            {
                reasonBuilder.Append(" The workload fits with acceptable headroom.");
            }

            return new AksPlacementAdvice
            {
                Mode = AksAdviceMode.Full,
                CanAssessNodeFit = true,
                CanAssessNeedForNewNode = true,
                FitsExistingNode = true,
                NeedsNewNode = shouldSuggestNewNode,
                RecommendedNode = candidate.NodeName,
                ShouldIncreaseCpuLimit = cpuLimitPressure,
                ShouldIncreaseMemoryLimit = memoryLimitPressure,
                RiskLevel = shouldSuggestNewNode ? "High" : riskLevel,
                Reason = reasonBuilder.ToString()
            };
        }

        private AksPlacementAdvice EvaluateSpecOnly(
            double cpuRequestCores,
            double memoryRequestMb,
            double cpuLimitCores,
            double memoryLimitMb,
            IReadOnlyCollection<AksNodeSnapshot> nodes)
        {
            var candidate = nodes
                .Where(n => cpuRequestCores <= n.FreeCpuByRequests && memoryRequestMb <= n.FreeMemoryByRequestsMb)
                .OrderByDescending(n => n.FreeCpuByRequests - cpuRequestCores)
                .ThenByDescending(n => n.FreeMemoryByRequestsMb - memoryRequestMb)
                .FirstOrDefault();

            if (candidate is null)
            {
                return new AksPlacementAdvice
                {
                    Mode = AksAdviceMode.LimitOnly,
                    CanAssessNodeFit = nodes.Count > 0,
                    CanAssessNeedForNewNode = nodes.Count > 0,
                    FitsExistingNode = false,
                    NeedsNewNode = nodes.Count > 0,
                    RecommendedNode = null,
                    ShouldIncreaseCpuLimit = false,
                    ShouldIncreaseMemoryLimit = false,
                    RiskLevel = nodes.Count > 0 ? "High" : "Medium",
                    Reason = nodes.Count > 0
                        ? "Prometheus and Metrics API are unavailable. Based on Kubernetes workload requests, no existing AKS node has enough free requested capacity."
                        : "Prometheus and Metrics API are unavailable. Kubernetes workload requests and limits were collected, but node capacity could not be evaluated."
                };
            }

            var projectedCpuRatio = candidate.CpuAllocatableCores <= 0
                ? 1
                : (candidate.CpuRequestedCores + cpuRequestCores) / candidate.CpuAllocatableCores;

            var projectedMemoryRatio = candidate.MemoryAllocatableMb <= 0
                ? 1
                : (candidate.MemoryRequestedMb + memoryRequestMb) / candidate.MemoryAllocatableMb;

            var riskLevel = projectedCpuRatio >= 0.9 || projectedMemoryRatio >= 0.9
                ? "High"
                : projectedCpuRatio >= 0.75 || projectedMemoryRatio >= 0.75
                    ? "Medium"
                    : "Low";

            return new AksPlacementAdvice
            {
                Mode = AksAdviceMode.LimitOnly,
                CanAssessNodeFit = true,
                CanAssessNeedForNewNode = true,
                FitsExistingNode = true,
                NeedsNewNode = false,
                RecommendedNode = candidate.NodeName,
                ShouldIncreaseCpuLimit = false,
                ShouldIncreaseMemoryLimit = false,
                RiskLevel = riskLevel,
                Reason = cpuLimitCores > 0 || memoryLimitMb > 0
                    ? "Prometheus and Metrics API are unavailable. Placement advice is based on Kubernetes workload requests/limits and current requested node capacity."
                    : "Prometheus and Metrics API are unavailable. Placement advice is based on Kubernetes workload requests and current requested node capacity."
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
                Reason = "Managed Prometheus, Metrics API, and Kubernetes workload metrics are unavailable. Only limit pressure advice can be returned."
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

        private static string BuildLabelSelector(IDictionary<string, string>? labels)
        {
            if (labels is null || labels.Count == 0)
            {
                return string.Empty;
            }

            return string.Join(",", labels
                .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Key) && !string.IsNullOrWhiteSpace(kvp.Value))
                .Select(kvp => $"{kvp.Key}={kvp.Value}"));
        }

        private static bool IsRunningWorkloadPod(V1Pod pod, string workloadName)
        {
            var podName = pod.Metadata?.Name ?? string.Empty;
            var phase = pod.Status?.Phase ?? string.Empty;
            return podName.StartsWith(workloadName + "-", StringComparison.OrdinalIgnoreCase)
                   && string.Equals(phase, "Running", StringComparison.OrdinalIgnoreCase);
        }

        private static double ParseCpuCores(IDictionary<string, ResourceQuantity>? quantities, string key)
        {
            if (quantities is null || !quantities.TryGetValue(key, out var quantity) || quantity is null)
            {
                return 0;
            }

            return ParseCpuQuantityToCores(quantity.ToString());
        }

        private static double ParseMemoryMb(IDictionary<string, ResourceQuantity>? quantities, string key)
        {
            if (quantities is null || !quantities.TryGetValue(key, out var quantity) || quantity is null)
            {
                return 0;
            }

            return BytesToMb(ParseKubernetesMemoryBytes(quantity.ToString()));
        }

        private static double ParseCpuQuantityToCores(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return 0;
            }

            value = value.Trim();
            if (value.EndsWith('n'))
            {
                var nano = value[..^1];
                return double.TryParse(nano, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedNano)
                    ? parsedNano / 1_000_000_000d
                    : 0;
            }

            if (value.EndsWith('u'))
            {
                var micro = value[..^1];
                return double.TryParse(micro, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedMicro)
                    ? parsedMicro / 1_000_000d
                    : 0;
            }

            if (value.EndsWith('m'))
            {
                var milli = value[..^1];
                return double.TryParse(milli, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedMilli)
                    ? parsedMilli / 1000d
                    : 0;
            }

            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0;
        }

        private static double ParseKubernetesMemoryBytes(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return 0;
            }

            value = value.Trim();
            var binarySuffixes = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["Ki"] = 1024d,
                ["Mi"] = 1024d * 1024d,
                ["Gi"] = 1024d * 1024d * 1024d,
                ["Ti"] = 1024d * 1024d * 1024d * 1024d,
                ["Pi"] = 1024d * 1024d * 1024d * 1024d * 1024d,
                ["Ei"] = 1024d * 1024d * 1024d * 1024d * 1024d * 1024d
            };

            foreach (var suffix in binarySuffixes)
            {
                if (value.EndsWith(suffix.Key, StringComparison.OrdinalIgnoreCase))
                {
                    var numericPart = value[..^suffix.Key.Length];
                    return double.TryParse(numericPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                        ? parsed * suffix.Value
                        : 0;
                }
            }

            var decimalSuffixes = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["K"] = 1000d,
                ["M"] = 1000d * 1000d,
                ["G"] = 1000d * 1000d * 1000d,
                ["T"] = 1000d * 1000d * 1000d * 1000d,
                ["P"] = 1000d * 1000d * 1000d * 1000d * 1000d,
                ["E"] = 1000d * 1000d * 1000d * 1000d * 1000d * 1000d
            };

            foreach (var suffix in decimalSuffixes)
            {
                if (value.EndsWith(suffix.Key, StringComparison.OrdinalIgnoreCase))
                {
                    var numericPart = value[..^suffix.Key.Length];
                    return double.TryParse(numericPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                        ? parsed * suffix.Value
                        : 0;
                }
            }

            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var rawBytes)
                ? rawBytes
                : 0;
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

        private sealed class KubernetesWorkloadSpec
        {
            public int Replicas { get; set; }
            public V1PodTemplateSpec? Template { get; set; }
        }

        private sealed class KubernetesNodeMetricList
        {
            public List<KubernetesNodeMetricItem> Items { get; set; } = [];
        }

        private sealed class KubernetesNodeMetricItem
        {
            public KubernetesObjectMetadata? Metadata { get; set; }
            public Dictionary<string, string> Usage { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            public DateTimeOffset Timestamp { get; set; }
            public string Window { get; set; } = string.Empty;
        }

        private sealed class KubernetesObjectMetadata
        {
            public string Name { get; set; } = string.Empty;
        }
    }
}
