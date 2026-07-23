using Microsoft.Extensions.Options;
using Pat.Containers.CapacityAdvisor.Agents.Cloudflare;
using Pat.Containers.CapacityAdvisor.Contracts;
using Pat.Containers.CapacityAdvisor.Models;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace Pat.Containers.CapacityAdvisor.Services;

public sealed class CloudflareAdviceService : IAdviceExplanationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly CloudflareAiOptions _options;
    private readonly ILogger<CloudflareAdviceService> _logger;

    public CloudflareAdviceService(
        HttpClient httpClient,
        IOptions<CloudflareAiOptions> options,
        ILogger<CloudflareAdviceService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<LlmAdviceResponse?> GenerateAdviceAsync(
        LlmAdviceRequest request,
        CancellationToken cancellationToken = default)
    {
        var endpoint = _options.Url;

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
        httpRequest.Headers.Add("x-api-key", _options.ApiKey);

        var prompt = BuildPrompt(request);

        var payload = new
        {
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = """
                              You are an AIOps capacity advisor.
                              Give careful operational advice based on the provided facts.
                              Do not invent metrics.
                              Do not recommend actions outside the supplied evidence.
                              Focus on workload fit, saturation risk, and overcommitment risk.
                              For Kubernetes and AKS, treat resource requests as the basis for scheduling decisions.
                              If the prompt says node-fit telemetry is unavailable, do not infer node placement or scale-out requirements.
                              Return only valid JSON that matches the requested schema.
                              """
                },
                new
                {
                    role = "user",
                    content = prompt
                }
            },
            response_format = new
            {
                type = "json_schema",
                json_schema = new
                {
                    name = "capacity_advice",
                    schema = new
                    {
                        type = "object",
                        additionalProperties = false,
                        properties = new
                        {
                            severity = new
                            {
                                type = "string",
                                @enum = new[] { "low", "medium", "high" }
                            },
                            operatorSummary = new
                            {
                                type = "string"
                            },
                            recommendedAction = new
                            {
                                type = "string"
                            },
                            reasoning = new
                            {
                                type = "string"
                            },
                            followUpChecks = new
                            {
                                type = "array",
                                items = new { type = "string" }
                            }
                        },
                        required = new[]
                        {
                            "severity",
                            "operatorSummary",
                            "recommendedAction",
                            "reasoning",
                            "followUpChecks"
                        }
                    }
                }
            }
        };

        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(payload, JsonOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            LogCloudflareFailure(response.StatusCode, content);
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;

            if (root.TryGetProperty("success", out var successElement) &&
                successElement.ValueKind == JsonValueKind.False)
            {
                _logger.LogWarning("Cloudflare AI returned success=false. Body: {Body}", content);
                return null;
            }

            if (!root.TryGetProperty("result", out var resultElement))
            {
                _logger.LogWarning("Cloudflare AI response did not contain a result property. Body: {Body}", content);
                return null;
            }

            var jsonText = ExtractJsonText(resultElement);

            if (string.IsNullOrWhiteSpace(jsonText))
            {
                _logger.LogWarning("Cloudflare AI result did not contain usable JSON text. Body: {Body}", content);
                return null;
            }

            if (jsonText.Contains("JSON Mode couldn't be met", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Cloudflare AI could not satisfy JSON mode. Body: {Body}", content);
                return null;
            }

            var advice = JsonSerializer.Deserialize<LlmAdviceResponse>(jsonText, JsonOptions);

            if (advice is null ||
                string.IsNullOrWhiteSpace(advice.Severity) ||
                string.IsNullOrWhiteSpace(advice.OperatorSummary) ||
                string.IsNullOrWhiteSpace(advice.RecommendedAction) ||
                string.IsNullOrWhiteSpace(advice.Reasoning))
            {
                _logger.LogWarning("Cloudflare AI returned incomplete advice JSON. Body: {Body}", jsonText);
                return null;
            }

            return advice;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse Cloudflare AI JSON response. Body: {Body}", content);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected failure while parsing Cloudflare AI response.");
            return null;
        }
    }

    private void LogCloudflareFailure(System.Net.HttpStatusCode statusCode, string content)
    {
        if (content.Contains("JSON Mode couldn't be met", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Cloudflare AI JSON mode could not be satisfied. StatusCode: {StatusCode}, Body: {Body}",
                statusCode,
                content);
            return;
        }

        _logger.LogWarning(
            "Cloudflare AI request failed with status code {StatusCode}. Body: {Body}",
            statusCode,
            content);
    }

    private static string BuildPrompt(LlmAdviceRequest request)
    {
        return string.Equals(request.Platform, "AKS", StringComparison.OrdinalIgnoreCase)
            ? BuildAksPrompt(request)
            : BuildAcaPrompt(request);
    }

    private static string BuildAcaPrompt(LlmAdviceRequest request)
    {
        return
$"""
Assess this workload and provide concise operational advice.

Facts:
- Platform: {request.Platform}
- Workload: {request.WorkloadName}
- Replicas: {request.CurrentReplicas}
- CPU usage: {request.CpuUsagePercent:F1}%
- Memory usage: {request.MemoryUsagePercent:F1}%
- CPU request cores: {request.CpuRequestCores:F2}
- CPU limit cores: {request.CpuLimitCores:F2}
- Memory request MB: {request.MemoryRequestMb:F0}
- Memory limit MB: {request.MemoryLimitMb:F0}
- Deterministic status: {request.DeterministicStatus}
- Deterministic reason: {request.DeterministicReason}

Instructions:
- Consider fit and overcommitment risk.
- Mention whether the main concern is CPU, memory, or both.
- Keep the advice practical and short.
- Suggest follow-up operational checks.
- Do not invent missing telemetry.
""";
    }

    private static string BuildAksPrompt(LlmAdviceRequest request)
    {
        var builder = new StringBuilder();

        builder.AppendLine("Assess this AKS workload and provide concise operational advice.");
        builder.AppendLine();
        builder.AppendLine("Facts:");
        builder.AppendLine($"- Platform: {request.Platform}");
        builder.AppendLine($"- Workload: {request.WorkloadName}");
        builder.AppendLine($"- Replicas: {request.CurrentReplicas}");
        builder.AppendLine($"- Advice mode: {request.AdviceMode ?? "unknown"}");
        builder.AppendLine($"- CPU usage: {request.CpuUsagePercent:F1}%");
        builder.AppendLine($"- Memory usage: {request.MemoryUsagePercent:F1}%");
        builder.AppendLine($"- CPU request cores: {request.CpuRequestCores:F2}");
        builder.AppendLine($"- CPU limit cores: {request.CpuLimitCores:F2}");
        builder.AppendLine($"- Memory request MB: {request.MemoryRequestMb:F0}");
        builder.AppendLine($"- Memory limit MB: {request.MemoryLimitMb:F0}");
        builder.AppendLine($"- Can assess node fit: {request.CanAssessNodeFit}");
        builder.AppendLine($"- Can assess need for new node: {request.CanAssessNeedForNewNode}");
        builder.AppendLine($"- Fits existing node: {request.FitsExistingNode}");
        builder.AppendLine($"- Needs new node: {request.NeedsNewNode}");
        builder.AppendLine($"- Recommended node: {request.RecommendedNode ?? "n/a"}");
        builder.AppendLine($"- Deterministic status: {request.DeterministicStatus}");
        builder.AppendLine($"- Deterministic reason: {request.DeterministicReason}");

        if (request.Nodes.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Node facts: ");

            foreach (var node in request.Nodes)
        {
            builder.AppendLine(
                $"- Node {node.NodeName}: allocatable CPU {node.CpuAllocatableCores:F2} cores, allocatable memory {node.MemoryAllocatableMb:F0} MB, requested CPU {node.CpuRequestedCores:F2} cores, requested memory {node.MemoryRequestedMb:F0} MB, free CPU by requests {node.FreeCpuByRequests:F2} cores, free memory by requests {node.FreeMemoryByRequestsMb:F0} MB");
        }
    }

    builder.AppendLine();
        builder.AppendLine("Instructions:");
        builder.AppendLine("- For AKS, node fit is determined by resource requests, not limits.");
        builder.AppendLine("- Use only the supplied facts.");
        builder.AppendLine("- If advice mode is Full, you may comment on node fit and whether a new node is needed.");
        builder.AppendLine("- If advice mode is LimitOnly, do not comment on node fit or adding a node.");
        builder.AppendLine("- In LimitOnly mode, only advise whether CPU limit, memory limit, or both should be increased.");
        builder.AppendLine("- Mention whether the main concern is CPU, memory, both, or insufficient telemetry.");
        builder.AppendLine("- Keep the advice practical and short.");
        builder.AppendLine("- Suggest follow-up operational checks.");
        builder.AppendLine("- Do not invent missing telemetry.");

        return builder.ToString();
    }

    private static string? ExtractJsonText(JsonElement resultElement)
    {
        if (resultElement.ValueKind == JsonValueKind.String)
        {
            return resultElement.GetString();
        }

        if (resultElement.ValueKind == JsonValueKind.Object)
        {
            if (resultElement.TryGetProperty("response", out var responseElement) &&
                responseElement.ValueKind == JsonValueKind.String)
            {
                return responseElement.GetString();
            }

            if (resultElement.TryGetProperty("output_text", out var outputTextElement) &&
                outputTextElement.ValueKind == JsonValueKind.String)
            {
                return outputTextElement.GetString();
            }

            if (resultElement.TryGetProperty("content", out var contentElement) &&
                contentElement.ValueKind == JsonValueKind.String)
            {
                return contentElement.GetString();
            }
        }

        return null;
    }
}