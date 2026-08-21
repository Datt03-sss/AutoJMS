using AutoJMS.DataHub.Api.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AutoJMS.DataHub.Api.Auth;

public static class IdentityServiceCollectionExtensions
{
    public static IServiceCollection AddDataHubIdentity(this IServiceCollection services, DataHubRuntimeOptions options)
    {
        services.AddSingleton(options);
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<IDeviceTokenService, HmacDeviceTokenService>();
        services.AddSingleton<HmacLicenseAssertionService>();
        services.AddSingleton<ILicenseAssertionValidator>(sp => sp.GetRequiredService<HmacLicenseAssertionService>());

        if (StagingTestIssuerPolicy.IsEnabled(options.EnvironmentName, options.AllowStagingTestIssuer))
            services.AddSingleton<IStagingTestLicenseAssertionIssuer>(sp => sp.GetRequiredService<HmacLicenseAssertionService>());

        return services;
    }
}
