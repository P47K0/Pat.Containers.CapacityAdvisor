using Microsoft.Extensions.Options;
using Pat.Containers.CapacityAdvisor.Agents.Cloudflare;

namespace Pat.Containers.CapacityAdvisor.Options;

public sealed class CloudflareAiOptionsValidator : IValidateOptions<CloudflareAiOptions>
{
    public ValidateOptionsResult Validate(string? name, CloudflareAiOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.AccountId))
            failures.Add("CloudflareAi:AccountId is required.");

        if (string.IsNullOrWhiteSpace(options.ApiToken))
            failures.Add("CloudflareAi:ApiToken is required.");

        if (string.IsNullOrWhiteSpace(options.Model))
            failures.Add("CloudflareAi:Model is required.");

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}