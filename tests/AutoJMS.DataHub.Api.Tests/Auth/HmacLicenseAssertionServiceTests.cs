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
            AllowStagingTestIssuer = true,
            LicenseAssertionValidationKey = new string('l', 32)
        }, TimeProvider.System);

        Assert.Throws<InvalidOperationException>(() => service.Issue(
            new StagingLicenseAssertionDescriptor(["272C03"], DateTimeOffset.UtcNow.AddMinutes(5), null, 1, 1)));
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
