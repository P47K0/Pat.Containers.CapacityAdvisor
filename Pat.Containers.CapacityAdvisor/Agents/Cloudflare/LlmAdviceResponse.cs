namespace Pat.Containers.CapacityAdvisor.Models;

public sealed class LlmAdviceResponse
{
    public string Severity { get; init; } = default!;
    public string OperatorSummary { get; init; } = default!;
    public string RecommendedAction { get; init; } = default!;
    public string Reasoning { get; init; } = default!;
    public string[] FollowUpChecks { get; init; } = Array.Empty<string>();
}