using Polly;
using Polly.Extensions.Http;
using SmartCharger.Api.Models;
using SmartCharger.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Polly: exponential backoff 2s → 4s → 8s + 10s timeout per attempt
var retryPolicy   = HttpPolicyExtensions.HandleTransientHttpError()
    .WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));
var timeoutPolicy = Policy.TimeoutAsync<HttpResponseMessage>(10);

builder.Services.AddHttpClient<ElspotService>()
    .AddPolicyHandler(timeoutPolicy)
    .AddPolicyHandler(retryPolicy);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
    c.SwaggerDoc("v1", new() { Title = "SmartCharger API", Version = "v1" }));

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();

app.UseCors();
app.UseSwagger();
app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "SmartCharger v1"));

// --- Minimal API endpoints ---

app.MapGet("/api/elspot/prices", async (ElspotService svc, string area = "DK2") =>
    Results.Ok(await svc.GetTodayPricesAsync(area)))
    .WithName("GetPrices");

app.MapGet("/api/elspot/co2", async (ElspotService svc, string area = "DK2") =>
    Results.Ok(await svc.GetCo2ForecastAsync(area)))
    .WithName("GetCo2Forecast");

app.MapGet("/api/elspot/merged", async (ElspotService svc, string area = "DK2") =>
    Results.Ok(await svc.GetMergedAsync(area)))
    .WithName("GetMerged");

app.MapGet("/api/elspot/recommendations", async (
    ElspotService svc,
    int hours = 4,
    string area = "DK2",
    OptimizationStrategy strategy = OptimizationStrategy.Cheapest) =>
    Results.Ok(await svc.GetRecommendationsAsync(hours, area, strategy)))
    .WithName("GetRecommendations");

app.MapGet("/api/elspot/window", async (
    ElspotService svc,
    int hours = 4,
    string area = "DK2",
    DateTime? deadline = null,
    OptimizationStrategy strategy = OptimizationStrategy.Cheapest) =>
{
    var window = await svc.GetBestWindowAsync(hours, area, deadline, strategy);
    return window is null
        ? Results.NotFound("Not enough data for the requested window.")
        : Results.Ok(window);
})
.WithName("GetBestWindow");

app.Run();
