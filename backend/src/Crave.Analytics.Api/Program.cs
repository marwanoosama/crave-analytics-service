using Crave.Analytics.Api.Health;
using Crave.Analytics.Api.Modules.Analytics.Endpoints;
using Crave.Analytics.Api.Modules.Analytics.Repositories;
using Crave.Analytics.Api.Modules.Analytics.Services;
using Crave.Analytics.Common.Messaging;
using Crave.Analytics.Infrastructure.Auth;
using Crave.Analytics.Infrastructure.Config;
using Crave.Analytics.Infrastructure.CoreClient;
using Crave.Analytics.Infrastructure.Events;
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
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient("core", client =>
{
    client.BaseAddress = new Uri(Env.CoreBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddSingleton<JwtVerifier>();
builder.Services.AddSingleton<CoreClient>();
builder.Services.AddSingleton<RbacClient>();
builder.Services.AddSingleton<PermissionCacheService>();
builder.Services.AddTransient<ErrorHandlerMiddleware>();
builder.Services.AddTransient<CorrelationIdMiddleware>();
builder.Services.AddSingleton(new ConsumerOptions(
    Env.RabbitUrl, Env.RabbitExchange, Env.RabbitQueue, Env.RabbitBindings,
    Env.RabbitDlx, Env.RabbitDlq, (ushort)Env.RabbitPrefetch));
builder.Services.AddSingleton<AmqpConsumer>();
builder.Services.AddSingleton<IEventDeduper, EventDeduper>();
builder.Services.AddSingleton<RestaurantDayRepository>();
builder.Services.AddSingleton<AnalyticsService>();
builder.Services.AddSingleton<IEventHandler, OrderPlacedHandler>();
builder.Services.AddSingleton<EventHandlerRegistry>();
builder.Services.AddHostedService<OrderEventsConsumerService>();

var app = builder.Build();

// Pipeline mirrors app.ts: correlation first (error logs carry it), then the
// error handler wrapping everything after it, then CORS.
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ErrorHandlerMiddleware>();
app.UseCors();

app.MapGet("/", () => ApiResponse.SendSuccess("crave-analytics-service"));
app.MapHealthEndpoints();
app.MapAnalyticsEndpoints();

// Index bootstrap is the DDL migration — fail fast if Mongo is unreachable.
await Indexes.EnsureAsync(app.Services.GetRequiredService<IMongoDatabase>());

app.Run();

