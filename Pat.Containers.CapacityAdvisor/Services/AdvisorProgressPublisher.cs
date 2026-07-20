using Microsoft.AspNetCore.SignalR;
using Pat.Containers.CapacityAdvisor.Contracts;
using Pat.Containers.CapacityAdvisor.Hubs;
using Pat.Containers.CapacityAdvisor.Models;

namespace Pat.Containers.CapacityAdvisor.Services;

public sealed class AdvisorProgressPublisher : IAdvisorProgressPublisher
{
    private readonly IHubContext<AdvisorHub> _hubContext;
    private readonly ILogger<AdvisorProgressPublisher> _logger;

    public AdvisorProgressPublisher(
        IHubContext<AdvisorHub> hubContext,
        ILogger<AdvisorProgressPublisher> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task PublishStatusAsync(
        AdvisorProgressMessage message,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Advisor status: {Step} - {Message}", message.Step, message.Message);

        await _hubContext.Clients.All.SendAsync(
            "statusChanged",
            message,
            cancellationToken);
    }

    public async Task PublishAssessmentReadyAsync(
        CapacityAssessment assessment,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Advisor assessment ready for workload {WorkloadName}",
            assessment.Snapshot?.WorkloadName);

        await _hubContext.Clients.All.SendAsync(
            "assessmentReady",
            assessment,
            cancellationToken);
    }

    public async Task PublishErrorAsync(
        AdvisorErrorMessage message,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("Advisor error: {Message}", message.Message);

        await _hubContext.Clients.All.SendAsync(
            "error",
            message,
            cancellationToken);
    }
}