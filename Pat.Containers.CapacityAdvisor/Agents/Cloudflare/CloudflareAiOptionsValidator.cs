using Microsoft.Extensions.Options;
using Pat.Containers.CapacityAdvisor.Agents.Cloudflare;

namespace Pat.Containers.CapacityAdvisor.Options;

public sealed class CloudflareAiOptionsValidator : IValidateOptions<CloudflareAiOptions>
{
    public ValidateOptionsResult Validate(string? name, CloudflareAiOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Url))
            failures.Add("CloudflareAi:Url is required.");

        if (string.IsNullOrWhiteSpace(options.ApiKey))
            failures.Add("CloudflareAi:ApiKey is required.");

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}