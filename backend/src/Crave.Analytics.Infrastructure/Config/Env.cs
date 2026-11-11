namespace Crave.Analytics.Infrastructure.Config;

public static class Env
{
    public static int Port { get; private set; }
    public static bool IsProduction { get; private set; }
    public static string[] CorsOrigins { get; private set; } = [];
    public static string AccessSecret { get; private set; } = "";
    public static string MongoUrl { get; private set; } = "";
    public static string MongoDatabase { get; private set; } = "";
    public static string RabbitUrl { get; private set; } = "";
    public static string RabbitExchange { get; private set; } = "";
    public static string RabbitQueue { get; private set; } = "";
    public static string[] RabbitBindings { get; private set; } = [];
    public static string RabbitDlx { get; private set; } = "";
    public static string RabbitDlq { get; private set; } = "";
    public static int RabbitPrefetch { get; private set; }
    public static string CoreBaseUrl { get; private set; } = "";
    public static string CoreInternalApiKey { get; private set; } = "";
    public static int RbacCacheTtlSec { get; private set; }
    public static int EventDedupeTtlDays { get; private set; }

    public static void Load()
    {
        Port = GetInt("PORT", 5000);
        IsProduction = GetString("ASPNETCORE_ENVIRONMENT", "development") == "production";
        CorsOrigins = GetList("CORS_ORIGINS", "http://localhost:3000");
        AccessSecret = Require("ACCESS_SECRET");
        MongoUrl = GetString("MONGO_URL", "mongodb://localhost:27017");
        MongoDatabase = GetString("MONGO_DATABASE", "crave_analytics");
        RabbitUrl = Require("RABBITMQ_URL");
        RabbitExchange = GetString("RABBITMQ_ORDER_EVENTS_EXCHANGE", "order.events");
        RabbitQueue = GetString("RABBITMQ_ORDER_EVENTS_QUEUE", "analytics-service.order-events");
        RabbitBindings = GetList("RABBITMQ_ORDER_EVENTS_BINDINGS", "order.#,payment.#");
        RabbitDlx = GetString("RABBITMQ_ORDER_EVENTS_DLX", "order.events.dlx");
        RabbitDlq = GetString("RABBITMQ_ORDER_EVENTS_DLQ", "analytics-service.order-events.dlq");
        RabbitPrefetch = GetInt("RABBITMQ_PREFETCH", 32);
        CoreBaseUrl = Require("CORE_SERVICE_BASE_URL");
        CoreInternalApiKey = Require("CORE_INTERNAL_API_KEY");
        RbacCacheTtlSec = GetInt("RBAC_CACHE_TTL_SEC", 300);
        EventDedupeTtlDays = GetInt("EVENT_DEDUPE_TTL_DAYS", 7);
    }

    private static string Require(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrEmpty(value))
            throw new InvalidOperationException($"Missing required environment variable: {name}");

        return value;
    }

    private static string GetString(string name, string fallback) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : fallback;

    private static int GetInt(string name, int fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrEmpty(value)) return fallback;
        if (!int.TryParse(value, out var parsed))
            throw new InvalidOperationException($"Environment variable {name} must be an integer, got \"{value}\"");
        return parsed;
    }

    private static string[] GetList(string name, string fallback) =>
        GetString(name, fallback)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
