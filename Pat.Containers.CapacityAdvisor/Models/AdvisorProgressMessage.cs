namespace Pat.Containers.CapacityAdvisor.Models;

public sealed class AdvisorProgressMessage
{
    public string Step { get; init; } = default!;
    public string Message { get; init; } = default!;
    public string Level { get; init; } = "info";
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
}