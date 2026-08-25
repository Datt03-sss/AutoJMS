using AutoJMS.DataHub.Api.Auth;
using AutoJMS.DataHub.Api.Configuration;

namespace AutoJMS.DataHub.Api.Tests.Auth;

public sealed class HmacLicenseAssertionServiceTests
{
    [Fact]
    public async Task Staging_issuer_produces_an_assertion_the_validator_accepts()
    {
        var now = new DateTimeOffset(2026, 8, 21, 2, 0, 0, TimeSpan.Zero);
        var service = new HmacLicenseAssertionService(new DataHubRuntimeOptions
        {
            Channel = "staging",
            EnvironmentName = "Staging",
            AllowStagingTestIssuer = true,
            StagingTestSigningKey = new string('s', 32),
            LicenseAssertionIssuer = "autojms-staging-test",
            LicenseAssertionAudience = "autojms-datahub-enroll"
        }, new FixedTimeProvider(now));

        var token = service.Issue(new StagingLicenseAssertionDescriptor(
            ["272C03"], now.AddMinutes(5), null, 1, 2));
        var result = await service.ValidateAsync(token, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("staging", result.Identity!.Channel);
        Assert.Contains("272C03", result.Identity.SiteCodes);
    }

    [Fact]
    public void Staging_issuer_fails_closed_when_not_enabled()
    {
        var service = new HmacLicenseAssertionService(new DataHubRuntimeOptions
        {
            Channel = "production",
            EnvironmentName = "Production",
            AllowStagingTestIssuer = true
        }, TimeProvider.System);

        Assert.Throws<InvalidOperationException>(() => service.Issue(
            new StagingLicenseAssertionDescriptor(["272C03"], DateTimeOffset.UtcNow.AddMinutes(5), null, 1, 1)));
    }

    [Fact]
    public async Task Hmac_validator_is_unavailable_outside_staging_even_with_key_material()
    {
        // Key material present, production channel: the HMAC validator must still
        // refuse. It is a staging integration seam, never a production trust root.
        var service = new HmacLicenseAssertionService(new DataHubRuntimeOptions
        {
            Channel = "production",
            EnvironmentName = "Production",
            StagingTestSigningKey = new string('p', 32)
        }, TimeProvider.System);

        var result = await service.ValidateAsync("v1.payload.signature", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("LICENSE_ASSERTION_UNAVAILABLE", result.FailureCode);
    }

    [Fact]
    public async Task Normalizes_site_codes_and_ignores_blank_values()
    {
        var now = new DateTimeOffset(2026, 8, 21, 2, 0, 0, TimeSpan.Zero);
        var service = new HmacLicenseAssertionService(new DataHubRuntimeOptions
        {
            Channel = "staging",
            EnvironmentName = "Staging",
            AllowStagingTestIssuer = true,
            StagingTestSigningKey = new string('s', 32),
            LicenseAssertionIssuer = "autojms-staging-test",
            LicenseAssertionAudience = "autojms-datahub-enroll"
        }, new FixedTimeProvider(now));
        var assertion = service.Issue(new StagingLicenseAssertionDescriptor(
            [" 272c03 ", "", "272C03"], now.AddMinutes(5), null, 2, 1));

        var result = await service.ValidateAsync(assertion, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Single(result.Identity!.SiteCodes);
        Assert.Contains("272C03", result.Identity.SiteCodes);
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
