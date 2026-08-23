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
        if (StagingTestIssuerPolicy.IsEnabled(options.EnvironmentName, options.AllowStagingTestIssuer)
            && string.Equals(options.Channel, DataHubRuntimeOptions.AllowedStagingChannel, StringComparison.Ordinal))
        {
            services.AddSingleton<ILicenseAssertionValidator>(sp => sp.GetRequiredService<HmacLicenseAssertionService>());
            services.AddSingleton<IStagingTestLicenseAssertionIssuer>(sp => sp.GetRequiredService<HmacLicenseAssertionService>());
        }
        else if (RsaLicenseAssertionValidator.HasKeyMaterial(options))
        {
            // Production path: verify assertions against the license issuer's RSA public key.
            // Opt-in by configuration only — no key material, no open enrollment.
            services.AddSingleton<ILicenseAssertionValidator, RsaLicenseAssertionValidator>();
        }
        else
        {
            // Do not silently treat the existing desktop token or an arbitrary
            // production HMAC as a DataHub license. Without DATAHUB_LICENSE_ASSERTION_PUBLIC_KEY
            // (or _PATH) enrollment stays closed.
            services.AddSingleton<ILicenseAssertionValidator, UnavailableLicenseAssertionValidator>();
        }

        return services;
    }
}
