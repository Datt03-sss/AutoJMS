using AutoJMS.DataHub.Api.Auth;
using AutoJMS.DataHub.Api.Configuration;
using AutoJMS.DataHub.Api.Domain;
using AutoJMS.DataHub.Api.Endpoints;
using AutoJMS.DataHub.Api.Health;
using AutoJMS.DataHub.Api.Hubs;
using AutoJMS.DataHub.Api.Infrastructure;
using AutoJMS.DataHub.Api.Services;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(serverOptions => serverOptions.Limits.MaxRequestBodySize = 1024 * 1024);
var runtimeOptions = DataHubRuntimeOptions.FromConfiguration(builder.Configuration, builder.Environment);

builder.Services.AddSingleton(runtimeOptions);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);
builder.Services.AddDataHubIdentity(runtimeOptions);
builder.Services.AddSingleton<PostgresDataSource>();
builder.Services.AddSingleton<IDataHubDatabaseProbe, NpgsqlDatabaseProbe>();
builder.Services.AddSingleton<LeaseRepository>();
builder.Services.AddSingleton<EnrollmentRepository>();
builder.Services.AddSingleton<DeviceRepository>();
builder.Services.AddSingleton<IngressIpRateLimiter>();
builder.Services.AddSingleton(JmsEventPolicyCatalog.Default);
builder.Services.AddSingleton<JmsEventPolicyRepository>();
builder.Services.AddSingleton<ProjectionReducer>();
builder.Services.AddSingleton<IngestRepository>();
builder.Services.AddSingleton<IngestPipeline>();
builder.Services.AddSingleton<IDoorbellPublisher, SignalRDoorbellPublisher>();
builder.Services.AddSingleton<ChangeRepository>();
builder.Services.AddSingleton<RetentionRepository>();
builder.Services.AddHostedService<RetentionHostedService>();
builder.Services.AddHealthChecks()
    .AddCheck<RuntimeConfigurationHealthCheck>("runtime-configuration", tags: ["ready"])
    .AddCheck<PostgresHealthCheck>("postgres", tags: ["ready"]);
builder.Services.AddSignalR(options => options.EnableDetailedErrors = false);
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    // Kestrel is private to the Compose edge network, so Caddy is the only public ingress and
    // the only trusted forwarded-header hop. Clearing BOTH lists does not express that — it
    // makes the middleware accept X-Forwarded-For from any peer, so a caller could forge its
    // client IP and dodge the per-IP enrollment/ingress limits. Trust the proxy ranges only.
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    foreach (var network in ParseTrustedProxyNetworks(runtimeOptions.TrustedProxyNetworks))
        options.KnownIPNetworks.Add(network);
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await ApiProblemWriter.WriteAsync(
            context.HttpContext,
            StatusCodes.Status429TooManyRequests,
            "RATE_LIMITED",
            "Too many requests; retry after the indicated delay.");
    };
    options.AddPolicy("device", context =>
    {
        var identity = context.GetDeviceIdentity();
        var partition = identity is null
            ? $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}"
            : $"device:{identity.DeviceId:D}";
        return RateLimitPartition.GetFixedWindowLimiter(partition, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 240,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });
    options.AddPolicy("enrollment", context => RateLimitPartition.GetFixedWindowLimiter(
        $"enroll:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});

static IReadOnlyList<System.Net.IPNetwork> ParseTrustedProxyNetworks(string configured)
{
    var parsed = new List<System.Net.IPNetwork>();
    foreach (var entry in (configured ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        if (System.Net.IPNetwork.TryParse(entry, out var network))
            parsed.Add(network);
        else
            Console.Error.WriteLine($"[DataHub] Ignoring malformed trusted proxy network '{entry}'.");
    }

    // Never fall back to "trust everyone": an unparseable override degrades to the defaults.
    if (parsed.Count == 0)
        foreach (var entry in DataHubRuntimeOptions.DefaultTrustedProxyNetworks.Split(','))
            parsed.Add(System.Net.IPNetwork.Parse(entry));

    return parsed;
}

var app = builder.Build();

app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var logger = errorApp.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger("DataHub.Errors");
    var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    if (exception is not null)
        logger.LogError(exception, "Unhandled DataHub request failure for {Path}.", context.Request.Path);

    await ApiProblemWriter.WriteAsync(
        context,
        StatusCodes.Status503ServiceUnavailable,
        ApiProblemCodes.ServiceUnavailable,
        "The DataHub dependency is temporarily unavailable.");
}));
app.UseForwardedHeaders();
app.UseMiddleware<IngressRateLimitMiddleware>();
app.UseMiddleware<DeviceAuthenticationMiddleware>();
app.UseRateLimiter();
app.UseMiddleware<DeviceStatusMiddleware>();

app.MapGet("/health/live", () => Results.Ok(new
{
    status = "Healthy",
    checks = new Dictionary<string, string> { ["process"] = "Healthy" }
}));

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResultStatusCodes = new Dictionary<HealthStatus, int>
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    },
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        // No `channel` here: /health/ready is unauthenticated, and naming the deployment channel
        // tells an anonymous caller which signing keys and license scope this host expects.
        await context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(pair => pair.Key, pair => pair.Value.Status.ToString())
        });
    }
});

app.MapEnrollmentEndpoints();
app.MapLeaseEndpoints();
app.MapIngestEndpoints();
app.MapSyncEndpoints();
app.MapHub<SiteHub>("/hubs/site").RequireRateLimiting("device");

app.Run();

public partial class Program;
