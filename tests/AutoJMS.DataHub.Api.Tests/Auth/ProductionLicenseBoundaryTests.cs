using AutoJMS.DataHub.Api.Auth;
using AutoJMS.DataHub.Api.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AutoJMS.DataHub.Api.Tests.Auth;

public sealed class ProductionLicenseBoundaryTests
{
    [Fact]
    public async Task Non_staging_validator_does_not_accept_a_production_hmac_assertion()
    {
        var services = new ServiceCollection();
        services.AddDataHubIdentity(new DataHubRuntimeOptions
        {
            EnvironmentName = "Production",
            Channel = "production",
            AllowStagingTestIssuer = true,
            StagingTestSigningKey = new string('s', 32),
            LicenseAssertionValidationKey = new string('p', 32)
        });
        await using var provider = services.BuildServiceProvider();
        var validator = provider.GetRequiredService<ILicenseAssertionValidator>();

        var result = await validator.ValidateAsync("v1.not-a-production-token.signature", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("LICENSE_ASSERTION_UNAVAILABLE", result.FailureCode);
    }

    [Fact]
    public async Task Staging_environment_cannot_enable_test_issuer_for_production_channel()
    {
        var services = new ServiceCollection();
        services.AddDataHubIdentity(new DataHubRuntimeOptions
        {
            EnvironmentName = "Staging",
            Channel = "production",
            AllowStagingTestIssuer = true,
            StagingTestSigningKey = new string('s', 32),
            LicenseAssertionValidationKey = new string('p', 32)
        });
        await using var provider = services.BuildServiceProvider();
        var validator = provider.GetRequiredService<ILicenseAssertionValidator>();

        var result = await validator.ValidateAsync("v1.not-a-production-token.signature", CancellationToken.None);

        Assert.IsType<UnavailableLicenseAssertionValidator>(validator);
        Assert.False(result.Succeeded);
        Assert.Equal("LICENSE_ASSERTION_UNAVAILABLE", result.FailureCode);
    }
}
