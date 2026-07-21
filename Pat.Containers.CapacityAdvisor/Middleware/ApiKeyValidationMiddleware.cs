namespace Pat.Containers.CapacityAdvisor.Middleware;

using Microsoft.AspNetCore.WebUtilities;

internal class ApiKeyCheckMiddleware
{
    private const string ApiKeyHeaderName = "X-ApiKey";
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyCheckMiddleware> _logger;
    private readonly string _apiKeyFromSettings;

    public ApiKeyCheckMiddleware(
        RequestDelegate next,
        ILogger<ApiKeyCheckMiddleware> logger,
        IConfiguration configuration)
    {
        _next = next;
        _logger = logger;
        _apiKeyFromSettings = configuration["AssessmentApiKey"] ?? string.Empty;
    }

    public async Task InvokeAsync(HttpContext httpContext)
    {
        var request = httpContext.Request;

        if (!request.Headers.TryGetValue(ApiKeyHeaderName, out var apiKeyValue))
        {
            _logger.LogWarning("No API key header found.");
            httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await WriteResponse(httpContext);
            return;
        }

        if (!string.Equals(apiKeyValue, _apiKeyFromSettings, StringComparison.Ordinal))
        {
            _logger.LogWarning("Invalid API key.");
            httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await WriteResponse(httpContext);
            return;
        }

        _logger.LogDebug("API key validated successfully.");
        await _next(httpContext);
    }

    private static async Task WriteResponse(HttpContext context)
    {
        if (context.Response.HasStarted
            || context.Response.ContentLength.HasValue
            || !string.IsNullOrEmpty(context.Response.ContentType))
        {
            return;
        }

        var body = $"Status Code: {context.Response.StatusCode}; {ReasonPhrases.GetReasonPhrase(context.Response.StatusCode)}";
        context.Response.ContentType = "text/plain";
        await context.Response.WriteAsync(body);
    }
}

public static class ApiKeyCheckMiddlewareExtensions
{
    public static IApplicationBuilder UseApiKeyCheckMiddleware(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ApiKeyCheckMiddleware>();
    }
}