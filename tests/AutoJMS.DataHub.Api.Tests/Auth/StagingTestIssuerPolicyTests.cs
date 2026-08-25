using AutoJMS.DataHub.Api.Auth;
using AutoJMS.DataHub.Api.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AutoJMS.DataHub.Api.Tests.Auth;

public sealed class StagingTestIssuerPolicyTests
{
    [Theory]
    [InlineData("Staging", true, true)]
    [InlineData("Staging", false, false)]
    [InlineData("Production", true, false)]
    [InlineData("Development", true, false)]
    public void Issuer_is_enabled_only_by_an_explicit_staging_opt_in(
        string environmentName,
        bool allowFlag,
        bool expected)
    {
        Assert.Equal(expected, StagingTestIssuerPolicy.IsEnabled(environmentName, allowFlag));
    }

    [Fact]
    public void Registration_exposes_test_issuer_only_when_staging_opt_in_is_enabled()
    {
        var stagingServices = new ServiceCollection();
        stagingServices.AddSingleton(TimeProvider.System);
        stagingServices.AddDataHubIdentity(CreateOptions("Staging", allow: true));

        var productionServices = new ServiceCollection();
        productionServices.AddSingleton(TimeProvider.System);
        productionServices.AddDataHubIdentity(CreateOptions("Production", allow: true));

        using var stagingProvider = stagingServices.BuildServiceProvider();
        using var productionProvider = productionServices.BuildServiceProvider();
        Assert.NotNull(stagingProvider.GetService<IStagingTestLicenseAssertionIssuer>());
        Assert.Null(productionProvider.GetService<IStagingTestLicenseAssertionIssuer>());
        Assert.NotNull(stagingProvider.GetRequiredService<ILicenseAssertionValidator>());
        Assert.NotNull(productionProvider.GetRequiredService<ILicenseAssertionValidator>());
    }

    private static DataHubRuntimeOptions CreateOptions(string environment, bool allow) => new()
    {
        Channel = environment == "Staging" ? "staging" : "production",
        EnvironmentName = environment,
        AllowStagingTestIssuer = allow,
        DeviceTokenSigningKey = new string('d', 32),
        StagingTestSigningKey = new string('s', 32)
    };
}
