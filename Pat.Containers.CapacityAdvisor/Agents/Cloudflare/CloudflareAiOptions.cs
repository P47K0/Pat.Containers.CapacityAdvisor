namespace Pat.Containers.CapacityAdvisor.Agents.Cloudflare;

public sealed class CloudflareAiOptions
{
    public const string SectionName = "AiAgent";

    public string Url { get; init; } = default!;
    public string ApiKey { get; init; } = default!;
}