using Pat.Containers.CapacityAdvisor.Models.Webhook;

namespace Pat.Containers.CapacityAdvisor.Services
{
    public static class AlertTrendAdvisor
    {
        public static string BuildOperatorHint(string signalType, AlertTrendSummary trend)
        {
            if (trend.AlertCount <= 1)
            {
                return signalType switch
                {
                    "CpuThrottling" => "Single throttling alert; verify whether this was a transient burst before changing limits.",
                    "HighMemoryUsage" => "Single memory alert; monitor closely before changing limits because the spike may be temporary.",
                    _ => "Single alert; verify before taking action."
                };
            }

            if (trend.IsWorsening)
            {
                return signalType switch
                {
                    "CpuThrottling" => "Repeated throttling alerts with worsening values; increase CPU limit/request or investigate cluster headroom.",
                    "HighMemoryUsage" => "Repeated high-memory alerts with worsening values; increase memory limit/request or scale out before OOM risk grows.",
                    _ => "Repeated alerts with worsening values; investigate capacity."
                };
            }

            return signalType switch
            {
                "CpuThrottling" => "Repeated throttling alerts detected; review CPU sizing and current node fit.",
                "HighMemoryUsage" => "Repeated high-memory alerts detected; review memory sizing and runtime pressure.",
                _ => "Repeated alerts detected; review trend."
            };
        }
    }
}
