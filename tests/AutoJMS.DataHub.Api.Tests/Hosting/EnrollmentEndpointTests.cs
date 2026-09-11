using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AutoJMS.DataHub.Api.Auth;
using AutoJMS.DataHub.Api.Configuration;
using AutoJMS.DataHub.Api.Endpoints;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AutoJMS.DataHub.Api.Tests.Hosting;

public sealed class EnrollmentEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public EnrollmentEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseSetting(WebHostDefaults.EnvironmentKey, "Testing"));
    }

    /// <summary>
    /// Stands in for the RSA validator so these can reach enrollment without key material and
    /// without turning the staging test issuer on. Everything under test here begins after
    /// validation, so the assertion's contents are the only part that has to be real.
    /// </summary>
    private sealed class AcceptingLicenseValidator(LicenseAssertionIdentity identity) : ILicenseAssertionValidator
    {
        public ValueTask<LicenseAssertionValidationResult> ValidateAsync(string assertion, CancellationToken cancellationToken)
            => ValueTask.FromResult(LicenseAssertionValidationResult.Success(identity));
    }

    [RequiresDataHubDatabaseFact]
    public async Task Enrolling_into_an_unprovisioned_site_provisions_it_and_answers_201()
    {
        // The whole point of the change, seen from where a station sees it. This site code has
        // never been in `sites`; before auto-provision the answer was 404 NOT_FOUND and someone
        // had to SSH in and INSERT the row before the customer could activate.
        //
        // Asserted from out here rather than on the repository result alone because the
        // enrollment body is a contract the station parses: LicenseApiService hands siteId and
        // deviceToken straight to DataHubClient.Configure, so a 201 missing either would pass
        // a repository test and still leave the station unable to sync.
        var connectionString = RequiresDataHubDatabaseFactAttribute.ConnectionString!;
        var siteCode = DataHubTestDatabase.NewSiteCode();
        try
        {
            using var factory = WithLicenseFor(siteCode, connectionString);
            using var client = factory.CreateClient();

            using var response = await client.SendAsync(EnrollRequestFor(siteCode, "PC-01"));

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var siteId = document.RootElement.GetProperty("siteId").GetGuid();
            Assert.Equal(siteCode, document.RootElement.GetProperty("siteCode").GetString());
            Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("deviceToken").GetString()));
            Assert.Equal(siteId, await DataHubTestDatabase.FindSiteIdAsync(connectionString, siteCode));
        }
        finally
        {
            await DataHubTestDatabase.TryDeleteSiteAsync(connectionString, siteCode);
        }
    }

    [Fact]
    public async Task Enrolling_into_a_site_outside_the_signed_assertion_answers_403()
    {
        // The guard that keeps auto-provision from becoming "any valid licence may create any
        // site it names in the request body". The assertion below is signed for a different
        // site, so the request is refused at the edge and never reaches the repository — which
        // is why this one needs no database and runs on every build, unlike its neighbours.
        var licensedSiteCode = DataHubTestDatabase.NewSiteCode();
        var requestedSiteCode = DataHubTestDatabase.NewSiteCode();
        using var factory = WithLicenseFor(licensedSiteCode, connectionString: null);
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(EnrollRequestFor(requestedSiteCode, "PC-01"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(ApiProblemCodes.SiteNotLicensed, document.RootElement.GetProperty("code").GetString());
    }

    private static HttpRequestMessage EnrollRequestFor(string siteCode, string deviceName)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/devices/enroll")
        {
            Content = JsonContent.Create(new EnrollRequest(siteCode, deviceName, "operator"))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "signed-by-the-stub-validator");
        return request;
    }

    private WebApplicationFactory<Program> WithLicenseFor(string licensedSiteCode, string? connectionString)
        => _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<DataHubRuntimeOptions>();
            services.AddSingleton(new DataHubRuntimeOptions
            {
                Channel = DataHubRuntimeOptions.AllowedStagingChannel,
                EnvironmentName = "Testing",
                ConnectionString = connectionString ?? string.Empty,
                // Both are length-checked ahead of the query, and a short one fails the request
                // with 503 -- a status none of these tests want to see for that reason.
                DeviceTokenSigningKey = new string('d', 32),
                EnrollmentPepper = new string('p', 32),
                // Retention deletes rows, and this host may be pointed at a real database.
                // PeriodicTimer fires first after a full interval, so one longer than the
                // test's lifetime keeps the pass from ever running.
                RetentionInterval = TimeSpan.FromHours(12)
            });
            services.RemoveAll<ILicenseAssertionValidator>();
            services.AddSingleton<ILicenseAssertionValidator>(new AcceptingLicenseValidator(
                new LicenseAssertionIdentity(
                    DataHubRuntimeOptions.AllowedStagingChannel,
                    new HashSet<string>(StringComparer.Ordinal) { licensedSiteCode },
                    DateTimeOffset.UtcNow.AddHours(1),
                    null,
                    1,
                    1)));
        }));
}
