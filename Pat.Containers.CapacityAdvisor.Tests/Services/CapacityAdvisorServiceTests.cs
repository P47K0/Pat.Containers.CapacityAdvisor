using Microsoft.Extensions.Logging;
using Moq;
using Pat.Containers.CapacityAdvisor.Agents.Cloudflare;
using Pat.Containers.CapacityAdvisor.Contracts;
using Pat.Containers.CapacityAdvisor.Models;
using Pat.Containers.CapacityAdvisor.Services;
using Xunit;

namespace Pat.Containers.CapacityAdvisor.Tests.Services;

public sealed class CapacityAdvisorServiceTests
{
    [Fact]
    public async Task AssessAsync_WhenCpuIsHigh_ReturnsScaleUpRecommendation()
    {
        // Arrange
        var collectorMock = new Mock<IPlatformMetricCollector>();
        var adviceExplanationServiceMock = new Mock<IAdviceExplanationService>();
        var loggerMock = new Mock<ILogger<CapacityAdvisorService>>();

        collectorMock
            .Setup(x => x.CollectAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MetricCollectionResult
            {
                Success = true,
                Snapshot = new PlatformSnapshot
                {
                    Platform = "ACA",
                    EnvironmentName = "dev",
                    WorkloadName = "capacity-simulator",
                    CurrentReplicas = 1,
                    CpuUsageCores = 0.90,
                    CpuRequestCores = 0.25,
                    CpuLimitCores = 1.00,
                    MemoryUsageMb = 300,
                    MemoryRequestMb = 256,
                    MemoryLimitMb = 1024
                }
            });

        adviceExplanationServiceMock
            .Setup(x => x.GenerateAdviceAsync(
                It.IsAny<LlmAdviceRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((LlmAdviceResponse?)null);

        var service = new CapacityAdvisorService(
            collectorMock.Object,
            adviceExplanationServiceMock.Object,
            loggerMock.Object);

        // Act
        var result = await service.AssessAsync(CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Snapshot);
        Assert.Equal(90.00, result.CpuUsagePercent);
        Assert.Equal(29.30, result.MemoryUsagePercent, 2);
        Assert.Equal("ScaleUp", result.Recommendation.Status);
        Assert.Equal(1.50, result.Recommendation.SuggestedCpuLimitCores);
        Assert.Equal(1536, result.Recommendation.SuggestedMemoryLimitMb);
    }
}