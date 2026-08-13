using Microsoft.Extensions.Options;
using Pat.Containers.CapacityAdvisor.Agents.Cloudflare;
using Pat.Containers.CapacityAdvisor.Contracts;
using Pat.Containers.CapacityAdvisor.Models;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Pat.Containers.CapacityAdvisor.Services;

public sealed class CloudflareAdviceService : IAdviceExplanationService
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
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
        ArgumentNullException.ThrowIfNull(request);

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            _options.Url);

        httpRequest.Headers.Add(
            "x-api-key",
            _options.ApiKey);

        var systemPrompt = BuildSystemPrompt(request);
        var userPrompt = BuildPrompt(request);

        var payload = new
        {
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = systemPrompt
                },
                new
                {
                    role = "user",
                    content = userPrompt
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
                                @enum = new[]
                                {
                                    "low",
                                    "medium",
                                    "high"
                                }
                            },
                            historicalTrend = new {
                                type = "string"
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
                                items = new
                                {
                                    type = "string"
                                }
                            }
                        },
                        required = new[]
                        {
                            "severity",
                            "historicalTrend",
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

        using var response = await _httpClient.SendAsync(
            httpRequest,
            cancellationToken);

        var content = await response.Content
            .ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            LogCloudflareFailure(
                response.StatusCode,
                content);

            return null;
        }

        return ParseAdviceResponse(content);
    }

    private static string BuildSystemPrompt(
    LlmAdviceRequest request)
    {
        var platformInstructions =
            string.Equals(
                request.Platform,
                "AKS",
                StringComparison.OrdinalIgnoreCase)
                ? """
              AKS rules:
              - Use AKS node and cluster information only when supplied.
              - Distinguish scheduling fit from runtime resource saturation.
              - In Full advice mode, explain node fit or scale-out risk only
                when the supplied evidence supports it.
              - In LimitOnly advice mode, do not recommend adding a node.
              - Do not recommend a node when the deterministic recommendation
                says that the workload fits.
              - Do not infer node pressure from workload usage alone.
              """
                : """
              Platform rules:
              - Do not infer Kubernetes node placement.
              - Do not recommend adding a node when node-placement evidence
                is unavailable.
              - Focus on workload metrics, requests, limits, alert history,
                and the deterministic recommendation.
              - Do not recommend adding replicas unless replica-scaling
                evidence is explicitly supplied.
              """;

        return $"""
    You are a Kubernetes capacity-planning expert with experience in
    Kubernetes, Azure Kubernetes Service, Azure Monitor, and Prometheus alerts.

    Analyze only the capacity evidence supplied in the user message.
    Do not invent missing, null, or empty values.

    Kubernetes resource semantics:
    - A resource request is used for scheduling.
    - A resource limit is the maximum CPU or memory a container may consume.
    - CpuUsagePercentOfLimit is calculated as CPU usage divided by CPU limit.
    - MemoryUsagePercentOfLimit is calculated as memory usage divided by
      memory limit.
    - Usage percentages in the evidence are percentages of limits, not
      percentages of requests.
    - Never describe a percentage of a limit as a percentage of a request.
    - If both a request and a limit are provided, clearly distinguish them.
    - Use "near the limit" when usage is below but close to the limit.
    - Use "exceeds the limit" only when the raw usage is greater than the
      configured limit.

    Deterministic recommendation:
    - The deterministic recommendation is authoritative.
    - Do not change its status.
    - Do not change its recommended action.
    - Do not change its suggested CPU limit.
    - Do not change its suggested memory limit.
    - Do not calculate alternative resource values.
    - Do not replace a positive suggested value with zero.
    - Use the exact suggested CPU and memory values in recommendedAction.
    - If the suggested CPU limit is 0.75 cores and the suggested memory limit
      is 768 MB, recommendedAction must mention exactly 0.75 cores and 768 MB.
    - Do not use a generic action such as "increase resource limits" when
      exact suggested values are available.

    Historical alert analysis:
    - HistoricalAlerts contains compact summaries grouped by alert category.
    - Category identifies the alert rule or alert type.
    - Count is the number of alerts in that category.
    - RecentValuesPercent contains recent observed values when available.
    - Count equal to 1 means a single occurrence.
    - Count greater than 1 means recurring.
    - Analyze every alert category separately.
    - Do not apply one aggregate trend to all categories.
    - Do not describe different alert categories as one combined alert type.
    - Include the actual count for every category in historicalTrend.
    - If a category has no observed values, say that its values are unavailable.
    - Do not output a bare percentage symbol for a missing value.
    - Compare historical alert evidence with the current workload metrics.
    - Historical alerts alone do not prove that the workload is currently
      under pressure.
    - If current usage is high and matching alert categories are recurring,
      describe the condition as sustained or recurring pressure.
    - If current usage is normal after historical alerts, describe the
      workload as stable or recovered.
    - Do not recommend adding replicas unless replica evidence is supplied.
    - Do not recommend adding nodes unless node-placement evidence is supplied.

    Historical trend output:
    - The response must contain a non-empty historicalTrend field.
    - historicalTrend must describe every alert category separately.
    - historicalTrend must include the actual count for every category.
    - For example, if HighMemoryUsage has count 3 and
      ContainerCpuThrottlingHigh has count 2, return:
      "HighMemoryUsage occurred 3 times and is recurring, while
      ContainerCpuThrottlingHigh occurred 2 times and is recurring."
    - Do not return null or omit historicalTrend.
    - Include the same category-specific trend in reasoning.

    Response quality:
    - Explain the current workload state.
    - Explain the historical alert categories and their counts.
    - Explain whether each category is isolated or recurring.
    - Explain why the deterministic recommendation is appropriate.
    - Use Kubernetes terminology accurately.
    - Avoid repeating the same sentence in operatorSummary and reasoning.
    - Keep the response concise and operational.
    - Do not make unsupported claims about nodes, replicas, or cluster capacity.

    {platformInstructions}

    Response JSON requirements:
    - Return only valid JSON.
    - Do not return Markdown.
    - Do not return text outside the JSON object.
    - Include all of these fields:
      severity,
      historicalTrend,
      operatorSummary,
      recommendedAction,
      reasoning,
      followUpChecks.
    - historicalTrend must be a non-empty string.
    - recommendedAction must contain the exact deterministic CPU and memory
      values when those values are available.
    - followUpChecks must contain practical checks supported by the evidence.
    """;
    }

    private static string BuildPrompt(
        LlmAdviceRequest request)
    {
        var evidenceJson =
            JsonSerializer.Serialize(
                request,
                JsonOptions);

        return $"""
        Analyze the following capacity evidence.

        The evidence contains:
        - Current workload metrics.
        - Requests and limits.
        - Historical alert count and observed values.
        - The deterministic recommendation.
        - Suggested resource limits.
        - AKS placement and cluster capacity data when available.

        Do not treat missing, null, or empty fields as facts.

        Capacity evidence:
        ```json
        {evidenceJson}
        ```

        Use the deterministic recommendation as the authoritative action.
        Explain the recommendation rather than replacing it.
        """;
    }

    private LlmAdviceResponse? ParseAdviceResponse(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;

            if (root.TryGetProperty("success", out var successElement) &&
                successElement.ValueKind == JsonValueKind.False)
            {
                _logger.LogWarning(
                    "Cloudflare AI returned success=false. Body: {Body}",
                    content);

                return null;
            }

            if (!root.TryGetProperty("result", out var resultElement))
            {
                _logger.LogWarning(
                    "Cloudflare AI response did not contain a result property. Body: {Body}",
                    content);

                return null;
            }

            // Try to get response as object directly
            if (resultElement.ValueKind == JsonValueKind.Object &&
                resultElement.TryGetProperty("response", out var responseElement) &&
                responseElement.ValueKind == JsonValueKind.Object)
            {
                var advice = JsonSerializer.Deserialize<LlmAdviceResponse>(
                    responseElement,
                    JsonOptions);

                if (advice is not null &&
                    !string.IsNullOrWhiteSpace(advice.Severity) &&
                    !string.IsNullOrWhiteSpace(advice.OperatorSummary) &&
                    !string.IsNullOrWhiteSpace(advice.RecommendedAction) &&
                    !string.IsNullOrWhiteSpace(advice.Reasoning))
                {
                    return advice;
                }

                _logger.LogWarning(
                    "Cloudflare AI returned incomplete advice JSON. Body: {Body}",
                    responseElement.GetRawText());

                return null;
            }

            // Fallback to string extraction
            var jsonText = ExtractJsonText(resultElement);

            if (string.IsNullOrWhiteSpace(jsonText))
            {
                _logger.LogWarning(
                    "Cloudflare AI result did not contain usable JSON text. Body: {Body}",
                    content);

                return null;
            }

            if (jsonText.Contains("JSON Mode couldn't be met", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Cloudflare AI could not satisfy JSON mode. Body: {Body}",
                    content);

                return null;
            }

            var adviceFromString = JsonSerializer.Deserialize<LlmAdviceResponse>(
                jsonText,
                JsonOptions);

            if (adviceFromString is null ||
                string.IsNullOrWhiteSpace(adviceFromString.Severity) ||
                string.IsNullOrWhiteSpace(adviceFromString.OperatorSummary) ||
                string.IsNullOrWhiteSpace(adviceFromString.RecommendedAction) ||
                string.IsNullOrWhiteSpace(adviceFromString.Reasoning))
            {
                _logger.LogWarning(
                    "Cloudflare AI returned incomplete advice JSON. Body: {Body}",
                    jsonText);

                return null;
            }

            return adviceFromString;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to parse Cloudflare AI JSON response. Body: {Body}",
                content);

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unexpected failure while parsing Cloudflare AI response.");

            return null;
        }
    }

    private void LogCloudflareFailure(
        System.Net.HttpStatusCode statusCode,
        string content)
    {
        if (content.Contains(
                "JSON Mode couldn't be met",
                StringComparison.OrdinalIgnoreCase))
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

    private static string? ExtractJsonText(
        JsonElement resultElement)
    {
        if (resultElement.ValueKind ==
            JsonValueKind.String)
        {
            return resultElement.GetString();
        }

        if (resultElement.ValueKind ==
            JsonValueKind.Object)
        {
            if (resultElement.TryGetProperty(
                    "response",
                    out var responseElement) &&
                responseElement.ValueKind ==
                JsonValueKind.String)
            {
                return responseElement.GetString();
            }

            if (resultElement.TryGetProperty(
                    "output_text",
                    out var outputTextElement) &&
                outputTextElement.ValueKind ==
                JsonValueKind.String)
            {
                return outputTextElement.GetString();
            }

            if (resultElement.TryGetProperty(
                    "content",
                    out var contentElement) &&
                contentElement.ValueKind ==
                JsonValueKind.String)
            {
                return contentElement.GetString();
            }
        }

        return null;
    }
}