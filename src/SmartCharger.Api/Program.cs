using Microsoft.EntityFrameworkCore;
using Polly;
using Polly.Extensions.Http;
using SmartCharger.Api.Services;
using SmartCharger.Application.Interfaces;
using SmartCharger.Application.Services;
using SmartCharger.Domain.Models;
using SmartCharger.Infrastructure.Persistence;
using SmartCharger.Infrastructure.Repositories;

var builder = WebApplication.CreateBuilder(args);

// ── Resilience ────────────────────────────────────────────
var retryPolicy   = HttpPolicyExtensions.HandleTransientHttpError()
    .WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));
var timeoutPolicy = Policy.TimeoutAsync<HttpResponseMessage>(10);

// ── Infrastructure ────────────────────────────────────────
builder.Services.AddHttpClient<EnergidataRepository>()
    .AddPolicyHandler(timeoutPolicy)
    .AddPolicyHandler(retryPolicy);

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlite("Data Source=smartcharger.db"));

// ── Dependency Inversion (D in SOLID) ────────────────────
builder.Services.AddScoped<IElspotRepository,  EnergidataRepository>();
builder.Services.AddScoped<ISessionRepository, SqliteSessionRepository>();
builder.Services.AddScoped<IChargingService,   ChargingService>();
builder.Services.AddScoped<ISessionService,    SessionService>();
builder.Services.AddScoped<IForecastService,   ForecastService>();
builder.Services.AddHostedService<CacheWarmupService>();

// ── API ───────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
    c.SwaggerDoc("v1", new() { Title = "SmartCharger API", Version = "v1" }));

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();

app.UseCors();
app.UseSwagger();
app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "SmartCharger v1"));

// ── Elspot endpoints ──────────────────────────────────────
app.MapGet("/api/elspot/merged", async (IChargingService svc, string area = "DK2") =>
    Results.Ok(await svc.GetMergedAsync(area)));

app.MapGet("/api/elspot/recommendations", async (
    IChargingService svc, int hours = 4, string area = "DK2",
    OptimizationStrategy strategy = OptimizationStrategy.Cheapest) =>
    Results.Ok(await svc.GetRecommendationsAsync(hours, area, strategy)));

app.MapGet("/api/elspot/window", async (
    IChargingService svc, int hours = 4, string area = "DK2",
    DateTime? deadline = null,
    OptimizationStrategy strategy = OptimizationStrategy.Cheapest) =>
{
    var window = await svc.GetBestWindowAsync(hours, area, deadline, strategy);
    return window is null ? Results.NotFound() : Results.Ok(window);
});

app.MapGet("/api/elspot/forecast", async (IForecastService svc, string area = "DK2", int horizon = 24) =>
    Results.Ok(await svc.GetForecastAsync(area, horizon)));

// ── Session endpoints ─────────────────────────────────────
app.MapPost("/api/sessions", async (ISessionService svc, SmartCharger.Application.Interfaces.SaveSessionRequest req) =>
    Results.Ok(await svc.SaveAsync(req)));

app.MapGet("/api/sessions/stats",   async (ISessionService svc) => Results.Ok(await svc.GetStatsAsync()));
app.MapGet("/api/sessions/monthly", async (ISessionService svc) => Results.Ok(await svc.GetMonthlyStatsAsync()));
app.MapGet("/api/sessions/co2report", async (ISessionService svc, string? month = null) =>
{
    var report = await svc.GetCo2ReportAsync(month);
    return report is null ? Results.NotFound() : Results.Ok(report);
});

app.Run();
