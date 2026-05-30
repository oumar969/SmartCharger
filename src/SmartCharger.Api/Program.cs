using Microsoft.EntityFrameworkCore;
using Polly;
using Polly.Extensions.Http;
using SmartCharger.Api.Data;
using SmartCharger.Api.Models;
using SmartCharger.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Polly: exponential backoff 2s → 4s → 8s + 10s timeout
var retryPolicy   = HttpPolicyExtensions.HandleTransientHttpError()
    .WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));
var timeoutPolicy = Policy.TimeoutAsync<HttpResponseMessage>(10);

builder.Services.AddHttpClient<ElspotService>()
    .AddPolicyHandler(timeoutPolicy)
    .AddPolicyHandler(retryPolicy);

// SQLite
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlite("Data Source=smartcharger.db"));
builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<ForecastService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
    c.SwaggerDoc("v1", new() { Title = "SmartCharger API", Version = "v1" }));

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();

// Auto-create DB on startup
using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();

app.UseCors();
app.UseSwagger();
app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "SmartCharger v1"));

// ── Elspot ────────────────────────────────────────────────
app.MapGet("/api/elspot/prices", async (ElspotService svc, string area = "DK2") =>
    Results.Ok(await svc.GetTodayPricesAsync(area))).WithName("GetPrices");

app.MapGet("/api/elspot/co2", async (ElspotService svc, string area = "DK2") =>
    Results.Ok(await svc.GetCo2ForecastAsync(area))).WithName("GetCo2");

app.MapGet("/api/elspot/merged", async (ElspotService svc, string area = "DK2") =>
    Results.Ok(await svc.GetMergedAsync(area))).WithName("GetMerged");

app.MapGet("/api/elspot/recommendations", async (
    ElspotService svc, int hours = 4, string area = "DK2",
    OptimizationStrategy strategy = OptimizationStrategy.Cheapest) =>
    Results.Ok(await svc.GetRecommendationsAsync(hours, area, strategy)))
    .WithName("GetRecommendations");

app.MapGet("/api/elspot/window", async (
    ElspotService svc, int hours = 4, string area = "DK2",
    DateTime? deadline = null,
    OptimizationStrategy strategy = OptimizationStrategy.Cheapest) =>
{
    var window = await svc.GetBestWindowAsync(hours, area, deadline, strategy);
    return window is null ? Results.NotFound() : Results.Ok(window);
}).WithName("GetBestWindow");

// ── ML Forecast ───────────────────────────────────────────
app.MapGet("/api/elspot/forecast", async (
    ForecastService svc, string area = "DK2", int horizon = 24) =>
    Results.Ok(await svc.GetForecastAsync(area, horizon)))
    .WithName("GetForecast");

// ── Sessions ──────────────────────────────────────────────
app.MapPost("/api/sessions", async (SessionService svc, SaveSessionRequest req) =>
    Results.Ok(await svc.SaveAsync(req))).WithName("SaveSession");

app.MapGet("/api/sessions", async (SessionService svc) =>
    Results.Ok(await svc.GetRecentAsync())).WithName("GetSessions");

app.MapGet("/api/sessions/stats", async (SessionService svc) =>
    Results.Ok(await svc.GetStatsAsync())).WithName("GetStats");

app.Run();
