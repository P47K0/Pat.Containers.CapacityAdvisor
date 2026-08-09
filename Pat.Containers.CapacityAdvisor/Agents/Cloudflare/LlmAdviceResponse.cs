namespace Pat.Containers.CapacityAdvisor.Models;

public sealed class LlmAdviceResponse
{
    public string Severity { get; init; } = string.Empty;
    public string HistoricalTrend { get; init; } = string.Empty;
    public string OperatorSummary { get; init; } = string.Empty;
    public string RecommendedAction { get; init; } = string.Empty;
    public string Reasoning { get; init; } = string.Empty;
    public string[] FollowUpChecks { get; init; } = Array.Empty<string>();
}