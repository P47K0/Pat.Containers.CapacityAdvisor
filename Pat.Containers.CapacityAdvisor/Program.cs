using Microsoft.Extensions.Options;
using Pat.Containers.CapacityAdvisor.Agents.Cloudflare;
using Pat.Containers.CapacityAdvisor.Contracts;
using Pat.Containers.CapacityAdvisor.Hubs;
using Pat.Containers.CapacityAdvisor.Middleware;
using Pat.Containers.CapacityAdvisor.Options;
using Pat.Containers.CapacityAdvisor.Platform.Aca;
using Pat.Containers.CapacityAdvisor.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services
    .AddOptions<CloudflareAiOptions>()
    .Bind(builder.Configuration.GetSection(CloudflareAiOptions.SectionName))
    .ValidateOnStart();

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();

builder.Services.AddAcaMetricCollector(builder.Configuration);
builder.Services.AddScoped<ICapacityAdvisorService, CapacityAdvisorService>();
builder.Services.AddHttpClient<IAdviceExplanationService, CloudflareAdviceService>();
builder.Services.AddSingleton<IValidateOptions<CloudflareAiOptions>, CloudflareAiOptionsValidator>();

builder.Services.AddSignalR();
builder.Services.AddScoped<IAdvisorProgressPublisher, AdvisorProgressPublisher>();

var app = builder.Build();

app.MapHub<AdvisorHub>("/hubs/advisor");

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthorization();

app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();
app.MapHealthChecks("/health");

app.UseWhen(
    ctx => ctx.Request.Path.StartsWithSegments("/api/assessment/run"),
    branch => branch.UseMiddleware<ApiKeyCheckMiddleware>());

app.Run();
