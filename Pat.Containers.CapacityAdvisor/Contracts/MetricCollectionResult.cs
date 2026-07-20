using Pat.Containers.CapacityAdvisor.Models;

namespace Pat.Containers.CapacityAdvisor.Contracts;

public sealed class MetricCollectionResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public PlatformSnapshot? Snapshot { get; init; }

    public static MetricCollectionResult Ok(PlatformSnapshot snapshot) =>
        new()
        {
            Success = true,
            Snapshot = snapshot
        };

    public static MetricCollectionResult Failed(string errorMessage) =>
        new()
        {
            Success = false,
            ErrorMessage = errorMessage
        };
}