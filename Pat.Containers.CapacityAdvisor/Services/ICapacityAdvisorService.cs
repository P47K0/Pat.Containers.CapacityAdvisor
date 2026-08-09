using Pat.Containers.CapacityAdvisor.Models;
using Pat.Containers.CapacityAdvisor.Models.Webhook;
using Pat.Containers.CapacityAdvisor.Storage;

namespace Pat.Containers.CapacityAdvisor.Services;

public interface ICapacityAdvisorService
{
    Task<CapacityAssessment> AssessAsync(CancellationToken cancellationToken = default);

    Task AssessAsync(
        string clusterName,
        string @namespace,
        string workloadName,
        string signalType,
        AlertTrendSummary trend,
        CancellationToken cancellationToken);

    Task<CapacityAssessment> AssessFromAlertAsync(
        string clusterName,
        string @namespace,
        string workloadName,
        string signalType,
        AlertTrendSummary trend,
        CancellationToken cancellationToken);

    Task<CapacityAssessment> GetRecommendationAsync(
    CapacityStatusEntity storedStatus,
    IReadOnlyList<AlertHistoryEntity> recentAlerts,
    CancellationToken cancellationToken);
}