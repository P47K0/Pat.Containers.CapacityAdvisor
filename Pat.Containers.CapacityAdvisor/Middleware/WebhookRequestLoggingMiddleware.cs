namespace Pat.Containers.CapacityAdvisor.Middleware
{
    using System.Text;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Logging;

    public sealed class WebhookRequestLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<WebhookRequestLoggingMiddleware> _logger;

        public WebhookRequestLoggingMiddleware(
            RequestDelegate next,
            ILogger<WebhookRequestLoggingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (!context.Request.Path.StartsWithSegments("/api/webhook"))
            {
                await _next(context);
                return;
            }

            context.Request.EnableBuffering(
                bufferThreshold: 30 * 1024,
                bufferLimit: 256 * 1024);

            string body;

            using (var reader = new StreamReader(
                context.Request.Body,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                leaveOpen: true))
            {
                body = await reader.ReadToEndAsync();
            }

            context.Request.Body.Position = 0;

            _logger.LogInformation(
                "Webhook request received. TraceId={TraceId}, Method={Method}, Path={Path}, " +
                "ContentType={ContentType}, ContentLength={ContentLength}, Body={Body}",
                context.TraceIdentifier,
                context.Request.Method,
                context.Request.Path,
                context.Request.ContentType,
                context.Request.ContentLength,
                body);

            await _next(context);
        }
    }
}
