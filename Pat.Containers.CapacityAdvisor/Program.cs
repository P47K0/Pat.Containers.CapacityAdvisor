using Azure.Data.Tables;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using Pat.Containers.CapacityAdvisor.Agents.Cloudflare;
using Pat.Containers.CapacityAdvisor.Api.Security;
using Pat.Containers.CapacityAdvisor.Contracts;
using Pat.Containers.CapacityAdvisor.Hubs;
using Pat.Containers.CapacityAdvisor.Middleware;
using Pat.Containers.CapacityAdvisor.Options;
using Pat.Containers.CapacityAdvisor.Platform.Aca;
using Pat.Containers.CapacityAdvisor.Platform.Aks;
using Pat.Containers.CapacityAdvisor.Platform.Local;
using Pat.Containers.CapacityAdvisor.Services;
using Pat.Containers.CapacityAdvisor.Storage;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services
    .AddOptions<CloudflareAiOptions>()
    .Bind(builder.Configuration.GetSection(CloudflareAiOptions.SectionName))
    .ValidateOnStart();

builder.Services.AddControllers();

var webhookAuthMode = builder.Configuration["WebhookAuth:Mode"];

if (builder.Environment.IsDevelopment() &&
    string.Equals(webhookAuthMode, "Development", StringComparison.OrdinalIgnoreCase))
{
    builder.Services
        .AddAuthentication("Development")
        .AddScheme<AuthenticationSchemeOptions, DevelopmentAuthHandler>("Development", _ => { });

    builder.Services.AddAuthorizationBuilder()
        .AddPolicy("AzureMonitorSecureWebhook", policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.RequireRole("CapacityAdvisor.AlertSender");
        });
}
else
{
    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

    builder.Services.AddAuthorizationBuilder()
        .AddPolicy("AzureMonitorSecureWebhook", policy =>
        {
            policy.RequireAuthenticatedUser();
        });
}

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();

var platform = builder.Configuration["TargetPlatform"];

if (string.Equals(platform, "AKS", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddAksMetricCollector(builder.Configuration);
}
else if (string.Equals(platform, "Local", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddLocalMetricCollector(builder.Configuration);
}
else
{
    builder.Services.AddAcaMetricCollector(builder.Configuration);
}

builder.Services.AddScoped<ICapacityAdvisorService, CapacityAdvisorService>();
builder.Services.AddScoped<IAzureMonitorAlertService, AzureMonitorAlertService>();
builder.Services.AddHttpClient<IAdviceExplanationService, CloudflareAdviceService>();
builder.Services.AddSingleton<IValidateOptions<CloudflareAiOptions>, CloudflareAiOptionsValidator>();

builder.Services.AddSignalR();
builder.Services.AddScoped<IAdvisorProgressPublisher, AdvisorProgressPublisher>();

builder.Services.AddSingleton<TableClient>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var connectionString = config.GetConnectionString("AlertStorage");
    return new TableClient(connectionString, "AlertHistory");
});

builder.Services.AddHostedService<TableStorageInitializer>();

builder.Services.AddSingleton(_ => new TableClient(
    builder.Configuration.GetConnectionString("AlertStorage"),
    "AlertHistory"));

builder.Services.AddScoped<IAlertHistoryRepository, TableAlertHistoryRepository>();
builder.Services.AddScoped<ICapacityStatusRepository, TableCapacityStatusRepository>();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto |
        ForwardedHeaders.XForwardedHost;
});

var app = builder.Build();

app.UseForwardedHeaders();

app.Use(async (context, next) =>
{
    app.Logger.LogInformation(
        "Incoming request: {Method} {Path}, Scheme={Scheme}, ContentType={ContentType}, ContentLength={ContentLength}",
        context.Request.Method,
        context.Request.Path,
        context.Request.Scheme,
        context.Request.ContentType,
        context.Request.ContentLength);

    await next();

    app.Logger.LogInformation(
        "Request completed: {Method} {Path}, StatusCode={StatusCode}",
        context.Request.Method,
        context.Request.Path,
        context.Response.StatusCode);
});

app.MapHub<AdvisorHub>("/hubs/advisor");

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();

app.Use(async (context, next) =>
{
    var identity = context.User.Identity;

    app.Logger.LogInformation(
        "After authentication: IsAuthenticated={IsAuthenticated}, " +
        "AuthenticationType={AuthenticationType}, ClaimTypes={ClaimTypes}",
        identity?.IsAuthenticated,
        identity?.AuthenticationType,
        string.Join(", ", context.User.Claims.Select(c => c.Type)));

    await next();
});

app.UseAuthorization();

app.UseSwagger();
app.UseSwaggerUI();

app.UseMiddleware<WebhookRequestLoggingMiddleware>();

app.MapControllers();
app.MapHealthChecks("/health");

app.UseWhen(
    ctx => ctx.Request.Path.StartsWithSegments("/api/assessment/run") || ctx.Request.Path.StartsWithSegments("/api/metrics") || ctx.Request.Path.StartsWithSegments("/api/alerts/status") || ctx.Request.Path.StartsWithSegments("/api/alerts/recommendation"),
    branch => branch.UseMiddleware<ApiKeyCheckMiddleware>());

app.Run();
