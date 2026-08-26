using System.Net;
using AutoJMS.DataHub.Api.Configuration;
using AutoJMS.DataHub.Api.Infrastructure;
using AutoJMS.DataHub.Api.Manifests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AutoJMS.DataHub.Api.Tests.Hosting;

public sealed class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseSetting(WebHostDefaults.EnvironmentKey, "Testing"));
    }

    private sealed class ReachableDatabase : IDataHubDatabaseProbe
    {
        public Task<bool> CanConnectAsync(CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class WritableManifestRoot : IManifestRootProbe
    {
        public ManifestRootProbeResult Probe(string root) => new(ManifestRootState.Writable, null);
    }

    [Fact]
    public async Task Live_does_not_depend_on_database_or_secrets()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_returns_503_when_configuration_and_database_are_unavailable()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Ready_stays_in_rotation_when_the_only_fault_is_a_reportable_gap()
    {
        // Guards the Degraded => 200 mapping. Compose gates the caddy service on this
        // endpoint, so if Degraded ever 503s again, a host whose only fault is a closed
        // publish path would fail to bring the site online at all.
        using var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<DataHubRuntimeOptions>();
            services.AddSingleton(new DataHubRuntimeOptions
            {
                Channel = "staging",
                EnvironmentName = "Staging",
                ConnectionString = "Host=postgres;Database=datahub;Username=datahub;Password=test",
                DeviceTokenSigningKey = new string('d', 32),
                EnrollmentPepper = new string('e', 32),
                AllowStagingTestIssuer = true,
                StagingTestSigningKey = new string('s', 32),
                // The gap under test: publishing is closed, reads and enrollment are not.
                ManifestAdminToken = ""
            });
            services.RemoveAll<IDataHubDatabaseProbe>();
            services.AddSingleton<IDataHubDatabaseProbe>(new ReachableDatabase());
            services.RemoveAll<IManifestRootProbe>();
            services.AddSingleton<IManifestRootProbe>(new WritableManifestRoot());
        }));

        using var client = factory.CreateClient();
        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Degraded", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}
