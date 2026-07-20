using Microsoft.AspNetCore.Mvc;
using Pat.Containers.CapacityAdvisor.Contracts;
using Pat.Containers.CapacityAdvisor.Models;
using Pat.Containers.CapacityAdvisor.Services;

namespace Pat.Containers.CapacityAdvisor.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AssessmentController : ControllerBase
{
    private readonly ICapacityAdvisorService _capacityAdvisorService;
    private readonly IAdvisorProgressPublisher _progressPublisher;

    public AssessmentController(
        ICapacityAdvisorService capacityAdvisorService,
        IAdvisorProgressPublisher progressPublisher)
    {
        _capacityAdvisorService = capacityAdvisorService;
        _progressPublisher = progressPublisher;
    }

    [HttpGet]
    [ProducesResponseType(typeof(CapacityAssessment), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<CapacityAssessment>> Get(CancellationToken cancellationToken)
    {
        var assessment = await _capacityAdvisorService.AssessAsync(cancellationToken);

        if (!assessment.Success)
        {
            return Problem(
                detail: assessment.ErrorMessage,
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Capacity assessment failed");
        }

        return Ok(assessment);
    }

    [HttpPost("run")]
    [ProducesResponseType(typeof(CapacityAssessment), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<CapacityAssessment>> RunAssessment(CancellationToken cancellationToken)
    {
        try
        {
            await _progressPublisher.PublishStatusAsync(new AdvisorProgressMessage
            {
                Step = "start",
                Message = "Starting capacity assessment..."
            }, cancellationToken);

            await _progressPublisher.PublishStatusAsync(new AdvisorProgressMessage
            {
                Step = "metrics",
                Message = "Collecting platform metrics..."
            }, cancellationToken);

            var assessment = await _capacityAdvisorService.AssessAsync(cancellationToken);

            if (!assessment.Success)
            {
                var errorMessage = new AdvisorErrorMessage
                {
                    Message = "Capacity assessment failed.",
                    Detail = assessment.ErrorMessage
                };

                await _progressPublisher.PublishErrorAsync(errorMessage, cancellationToken);

                return Problem(
                    detail: assessment.ErrorMessage,
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "Capacity assessment failed");
            }

            await _progressPublisher.PublishStatusAsync(new AdvisorProgressMessage
            {
                Step = "analysis",
                Message = "Assessment completed. Preparing recommendation..."
            }, cancellationToken);

            await _progressPublisher.PublishAssessmentReadyAsync(assessment, cancellationToken);

            await _progressPublisher.PublishStatusAsync(new AdvisorProgressMessage
            {
                Step = "done",
                Message = "Recommendation ready."
            }, cancellationToken);

            return Ok(assessment);
        }
        catch (OperationCanceledException)
        {
            var errorMessage = new AdvisorErrorMessage
            {
                Message = "Capacity assessment was canceled."
            };

            await _progressPublisher.PublishErrorAsync(errorMessage, CancellationToken.None);

            return Problem(
                detail: "The request was canceled.",
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Assessment canceled");
        }
        catch (Exception ex)
        {
            var errorMessage = new AdvisorErrorMessage
            {
                Message = "Unexpected error while running capacity assessment.",
                Detail = ex.Message
            };

            await _progressPublisher.PublishErrorAsync(errorMessage, CancellationToken.None);

            return Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Unexpected assessment error");
        }
    }
}