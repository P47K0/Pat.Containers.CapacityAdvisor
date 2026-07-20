using Pat.Containers.CapacityAdvisor.Models;

namespace Pat.Containers.CapacityAdvisor.Contracts;

public interface IAdvisorProgressPublisher
{
    Task PublishStatusAsync(
        AdvisorProgressMessage message,
        CancellationToken cancellationToken = default);

    Task PublishAssessmentReadyAsync(
        CapacityAssessment assessment,
        CancellationToken cancellationToken = default);

    Task PublishErrorAsync(
        AdvisorErrorMessage message,
        CancellationToken cancellationToken = default);
}