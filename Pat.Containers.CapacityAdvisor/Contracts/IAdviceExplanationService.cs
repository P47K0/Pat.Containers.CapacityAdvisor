using Pat.Containers.CapacityAdvisor.Agents.Cloudflare;
using Pat.Containers.CapacityAdvisor.Models;

namespace Pat.Containers.CapacityAdvisor.Contracts;

public interface IAdviceExplanationService
{
    Task<LlmAdviceResponse?> GenerateAdviceAsync(
        LlmAdviceRequest request,
        CancellationToken cancellationToken = default);
}