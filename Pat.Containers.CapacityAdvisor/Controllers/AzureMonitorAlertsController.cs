using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pat.Containers.CapacityAdvisor.Models;
using Pat.Containers.CapacityAdvisor.Models.Webhook;
using Pat.Containers.CapacityAdvisor.Services;
using Pat.Containers.CapacityAdvisor.Storage;
using System.Collections.Concurrent;

namespace Pat.Containers.CapacityAdvisor.Controllers
{
    [ApiController]
    [Route("api/alerts/azure-monitor")]    
    public sealed class AzureMonitorAlertsController : ControllerBase
    {
        private readonly IAzureMonitorAlertService _alertService;
        private readonly ICapacityStatusRepository _statusRepository;
        private readonly ICapacityAdvisorService _capacityAdvisorService;
        private readonly IAlertHistoryRepository _alertHistoryRepository;
        private readonly ILogger<AzureMonitorAlertsController> _logger;

        public AzureMonitorAlertsController(ILogger<AzureMonitorAlertsController> logger, IAzureMonitorAlertService alertService, ICapacityStatusRepository statusRepository, ICapacityAdvisorService capacityAdvisorService, IAlertHistoryRepository alertHistoryRepository)
        {
            _alertService = alertService;
            _statusRepository = statusRepository;
            _capacityAdvisorService = capacityAdvisorService;
            _alertHistoryRepository = alertHistoryRepository;
            _logger = logger;
        }

        [HttpPost]
        [Authorize(Policy = "AzureMonitorSecureWebhook")]
        public async Task<IActionResult> ReceiveAsync(
            [FromBody] AzureMonitorCommonAlert payload,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation(
            "Webhook request: Scheme={Scheme}, Host={Host}, RemoteIp={RemoteIp}, " +
            "Method={Method}, Path={Path}, ContentType={ContentType}, ContentLength={ContentLength}",
            HttpContext.Request.Scheme,
            HttpContext.Request.Host,
            HttpContext.Connection.RemoteIpAddress,
            HttpContext.Request.Method,
            HttpContext.Request.Path,
            HttpContext.Request.ContentType,
            HttpContext.Request.ContentLength);

            if (payload?.Data?.Essentials is null)
            {
                _logger.LogWarning("Received invalid Azure Monitor common alert payload: {Payload}", payload);

                return BadRequest("Invalid Azure Monitor common alert payload.");
            }            

            await _alertService.HandleAsync(payload, cancellationToken);

            return Ok();
        }

        [HttpGet("status/{clusterName}/{namespace}/{workloadName}/{signalType}")]
        public async Task<ActionResult<CapacityStatusEntity>> GetStatusAsync(
        string clusterName,
        string @namespace,
        string workloadName,
        string signalType,
        CancellationToken cancellationToken)
        {
            var partitionKey = $"{clusterName}|{@namespace}|{workloadName}|{signalType}";
            var status = await _statusRepository.GetAsync(partitionKey, cancellationToken);

            return status is null ? NotFound() : Ok(status);
        }

        [HttpGet("recommendation/{clusterName}/{namespace}/{workloadName}/{signalType}")]
        public async Task<ActionResult<CapacityAssessment>> GetRecommendationAsync(
        string clusterName,
        string @namespace,
        string workloadName,
        string signalType,
        CancellationToken cancellationToken)
        {
            var partitionKey =
                $"{clusterName}|{@namespace}|{workloadName}|{signalType}";

            var storedStatus = await _statusRepository.GetAsync(
                partitionKey,
                cancellationToken);

            if (storedStatus is null)
            {
                return NotFound();
            }

            var recentAlerts = await _alertHistoryRepository
                .GetRecentForWorkloadAsync(
                    clusterName,
                    @namespace,
                    workloadName,
                    take: 10,
                    cancellationToken);

            var assessment = await _capacityAdvisorService
                .GetRecommendationAsync(
                    storedStatus,
                    recentAlerts,
                    cancellationToken);

            if (!assessment.Success)
            {
                return StatusCode(
                    StatusCodes.Status503ServiceUnavailable,
                    assessment);
            }

            return Ok(assessment);
        }

        [HttpGet("recommendation/{clusterName}/{namespace}/{workloadName}")]
        public async Task<ActionResult<CapacityAssessment>>
        GetRecommendationAsync(
            string clusterName,
            string @namespace,
            string workloadName,
            CancellationToken cancellationToken)
        {
            var partitionKey =
                $"{clusterName}|{@namespace}|{workloadName}";

            var storedStatus = await _statusRepository.GetAsync(
                partitionKey,
                cancellationToken);

            if (storedStatus is null)
            {
                return NotFound();
            }

            var recentAlerts =
                await _alertHistoryRepository.GetRecentForWorkloadAsync(
                    clusterName,
                    @namespace,
                    workloadName,
                    take: 20,
                    cancellationToken);

            if (recentAlerts.Count == 0)
            {
                return NotFound();
            }

            var assessment =
                await _capacityAdvisorService.GetRecommendationAsync(
                    storedStatus,
                    recentAlerts,
                    cancellationToken);

            if (!assessment.Success)
            {
                return StatusCode(
                    StatusCodes.Status503ServiceUnavailable,
                    assessment);
            }

            return Ok(assessment);
        }
    }
}
