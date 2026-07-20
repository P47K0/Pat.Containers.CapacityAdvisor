using Microsoft.AspNetCore.Mvc;
using Pat.Containers.CapacityAdvisor.Contracts;
using Pat.Containers.CapacityAdvisor.Models;

namespace Pat.Containers.CapacityAdvisor.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class MetricsController : ControllerBase
{
    private readonly IPlatformMetricCollector _collector;

    public MetricsController(IPlatformMetricCollector collector)
    {
        _collector = collector;
    }

    [HttpGet]
    public async Task<ActionResult<PlatformSnapshot>> Get(CancellationToken cancellationToken)
    {
        var result = await _collector.CollectAsync(cancellationToken);

        if (!result.Success || result.Snapshot is null)
        {
            return Problem(
                detail: result.ErrorMessage,
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Metric collection failed");
        }

        return Ok(result.Snapshot);
    }
}