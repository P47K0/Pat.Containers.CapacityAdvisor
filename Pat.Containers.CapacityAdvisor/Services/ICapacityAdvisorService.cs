using Pat.Containers.CapacityAdvisor.Models;

namespace Pat.Containers.CapacityAdvisor.Services;

public interface ICapacityAdvisorService
{
    Task<CapacityAssessment> AssessAsync(CancellationToken cancellationToken = default);
}