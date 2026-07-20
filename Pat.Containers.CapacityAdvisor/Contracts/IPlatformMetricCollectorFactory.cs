using Pat.Containers.CapacityAdvisor.Contracts;

public interface IPlatformMetricCollectorFactory
{
    IPlatformMetricCollector Get(string platform);
}