using Azure.Data.Tables;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
            policy.RequireClaim("roles", "CapacityAdvisor.AlertSender");
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

var app = builder.Build();

app.MapHub<AdvisorHub>("/hubs/advisor");

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();
app.MapHealthChecks("/health");

app.UseWhen(
    ctx => ctx.Request.Path.StartsWithSegments("/api/assessment/run") || ctx.Request.Path.StartsWithSegments("/api/metrics") || ctx.Request.Path.StartsWithSegments("/api/alerts/status") || ctx.Request.Path.StartsWithSegments("/api/alerts/recommendation"),
    branch => branch.UseMiddleware<ApiKeyCheckMiddleware>());

app.Run();
