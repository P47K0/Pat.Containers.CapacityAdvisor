namespace Pat.Containers.CapacityAdvisor.Agents.Cloudflare
{
    public sealed class LlmAlertSummary
    {
        public string Category { get; init; } = string.Empty;

        public int Count { get; init; }

        public IReadOnlyList<double> RecentValuesPercent { get; init; } =
            Array.Empty<double>();
    }
}
