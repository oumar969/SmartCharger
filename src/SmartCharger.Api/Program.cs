using Polly;
using Polly.Extensions.Http;
using SmartCharger.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Polly retry policy: 3 retries with exponential backoff (2s, 4s, 8s)
var retryPolicy = HttpPolicyExtensions
    .HandleTransientHttpError()
    .WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));

var timeoutPolicy = Policy.TimeoutAsync<HttpResponseMessage>(10);

builder.Services.AddHttpClient<ElspotService>()
    .AddPolicyHandler(timeoutPolicy)
    .AddPolicyHandler(retryPolicy);

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();

app.UseCors();

// --- Minimal API endpoints ---

app.MapGet("/api/elspot/prices", async (ElspotService elspot, string area = "DK2") =>
    Results.Ok(await elspot.GetTodayPricesAsync(area)));

app.MapGet("/api/elspot/recommendations", async (
    ElspotService elspot, int hours = 4, string area = "DK2") =>
    Results.Ok(await elspot.GetChargeRecommendationsAsync(hours, area)));

app.MapGet("/api/elspot/window", async (
    ElspotService elspot, int hours = 4, string area = "DK2", DateTime? deadline = null) =>
{
    var window = await elspot.GetBestWindowAsync(hours, area, deadline);
    return window is null
        ? Results.NotFound("Not enough data for the requested window.")
        : Results.Ok(window);
});

app.Run();
