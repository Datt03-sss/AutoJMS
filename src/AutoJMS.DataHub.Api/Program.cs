using AutoJMS.DataHub.Api.Auth;
using AutoJMS.DataHub.Api.Configuration;
using AutoJMS.DataHub.Api.Endpoints;
using AutoJMS.DataHub.Api.Health;
using AutoJMS.DataHub.Api.Hubs;
using AutoJMS.DataHub.Api.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);
var runtimeOptions = DataHubRuntimeOptions.FromConfiguration(builder.Configuration, builder.Environment);

builder.Services.AddSingleton(runtimeOptions);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDataHubIdentity(runtimeOptions);
builder.Services.AddSingleton<PostgresDataSource>();
builder.Services.AddSingleton<IDataHubDatabaseProbe, NpgsqlDatabaseProbe>();
builder.Services.AddHealthChecks()
    .AddCheck<RuntimeConfigurationHealthCheck>("runtime-configuration", tags: ["ready"])
    .AddCheck<PostgresHealthCheck>("postgres", tags: ["ready"]);
builder.Services.AddSignalR(options => options.EnableDetailedErrors = false);

var app = builder.Build();

app.UseMiddleware<DeviceAuthenticationMiddleware>();

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
        await context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(pair => pair.Key, pair => pair.Value.Status.ToString()),
            channel = runtimeOptions.Channel
        });
    }
});

app.MapEnrollmentEndpoints();
app.MapHub<SiteHub>("/hubs/site");

app.Run();

public partial class Program;
