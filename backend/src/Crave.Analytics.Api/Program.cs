using Crave.Analytics.Api.Health;
using Crave.Analytics.Api.Modules.Analytics.Repositories;
using Crave.Analytics.Infrastructure.Config;
using Crave.Analytics.Infrastructure.Http;
using Crave.Analytics.Infrastructure.Mongo;
using MongoDB.Driver;

// Fail before anything else — a missing/malformed var names itself here.
Env.Load();

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls($"http://*:{Env.Port}");
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.UseUtcTimestamp = true);

builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(Env.CorsOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
builder.Services.AddMongo();
builder.Services.AddTransient<ErrorHandlerMiddleware>();
builder.Services.AddTransient<CorrelationIdMiddleware>();

var app = builder.Build();

// Pipeline mirrors app.ts: correlation first (error logs carry it), then the
// error handler wrapping everything after it, then CORS.
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ErrorHandlerMiddleware>();
app.UseCors();

app.MapGet("/", () => ApiResponse.SendSuccess("crave-analytics-service"));
app.MapHealthEndpoints();

// Index bootstrap is the DDL migration — fail fast if Mongo is unreachable.
await Indexes.EnsureAsync(app.Services.GetRequiredService<IMongoDatabase>());

app.Run();

