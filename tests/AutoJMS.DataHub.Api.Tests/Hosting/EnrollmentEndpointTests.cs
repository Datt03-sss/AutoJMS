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
    /// Stands in for the RSA validator so the test can reach enrollment without key
    /// material and without turning the staging test issuer on. What is under test is the
    /// status the repository's site lookup produces, which begins after validation.
    /// </summary>
    private sealed class AcceptingLicenseValidator(LicenseAssertionIdentity identity) : ILicenseAssertionValidator
    {
        public ValueTask<LicenseAssertionValidationResult> ValidateAsync(string assertion, CancellationToken cancellationToken)
            => ValueTask.FromResult(LicenseAssertionValidationResult.Success(identity));
    }

    [RequiresDataHubDatabaseFact]
    public async Task Enrolling_into_an_unprovisioned_site_answers_404_rather_than_503()
    {
        // The bug this guards was only visible from out here. EnrollAsync reached
        // RollbackAsync with the site lookup's reader still open, Npgsql threw because one
        // command may be in progress per connector, and Program.cs's catch-all turned every
        // unhandled exception into 503 SERVICE_UNAVAILABLE -- so a caller naming a site that
        // was never provisioned was told the backend was down. Asserting the repository
        // result alone cannot see that: the 503 is produced by the error handler, not by
        // the branch.
        var siteCode = "NOSUCH" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        using var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<DataHubRuntimeOptions>();
            services.AddSingleton(new DataHubRuntimeOptions
            {
                Channel = DataHubRuntimeOptions.AllowedStagingChannel,
                EnvironmentName = "Testing",
                ConnectionString = RequiresDataHubDatabaseFactAttribute.ConnectionString!,
                // Both are length-checked ahead of the query, and a short one fails the
                // request with 503 -- which is the status this test exists to rule out.
                DeviceTokenSigningKey = new string('d', 32),
                EnrollmentPepper = new string('p', 32),
                // Retention deletes rows, and this host is pointed at a real database.
                // PeriodicTimer fires first after a full interval, so one longer than the
                // test's lifetime keeps the pass from ever running.
                RetentionInterval = TimeSpan.FromHours(12)
            });
            services.RemoveAll<ILicenseAssertionValidator>();
            services.AddSingleton<ILicenseAssertionValidator>(new AcceptingLicenseValidator(
                new LicenseAssertionIdentity(
                    DataHubRuntimeOptions.AllowedStagingChannel,
                    new HashSet<string>(StringComparer.Ordinal) { siteCode },
                    DateTimeOffset.UtcNow.AddHours(1),
                    null,
                    1,
                    1)));
        }));

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/devices/enroll")
        {
            Content = JsonContent.Create(new EnrollRequest(siteCode, "PC-01", "operator"))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "signed-by-the-stub-validator");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(ApiProblemCodes.NotFound, document.RootElement.GetProperty("code").GetString());
    }
}
