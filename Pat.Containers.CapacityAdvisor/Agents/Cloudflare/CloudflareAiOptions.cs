namespace Pat.Containers.CapacityAdvisor.Agents.Cloudflare;

public sealed class CloudflareAiOptions
{
    public const string SectionName = "CloudflareAi";

    public string AccountId { get; init; } = default!;
    public string ApiToken { get; init; } = default!;
    public string Model { get; init; } = "@cf/meta/llama-3.1-8b-instruct";
}