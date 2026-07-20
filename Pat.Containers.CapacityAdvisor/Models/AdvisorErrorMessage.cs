namespace Pat.Containers.CapacityAdvisor.Models;

public sealed class AdvisorErrorMessage
{
    public string Message { get; init; } = default!;
    public string? Detail { get; init; }
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
}