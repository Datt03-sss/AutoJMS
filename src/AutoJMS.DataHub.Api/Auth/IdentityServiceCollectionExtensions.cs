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
        bool stagingTestIssuer = StagingTestIssuerPolicy.IsEnabled(options.EnvironmentName, options.AllowStagingTestIssuer)
            && string.Equals(options.Channel, DataHubRuntimeOptions.AllowedStagingChannel, StringComparison.Ordinal);

        if (stagingTestIssuer)
        {
            services.AddSingleton<IStagingTestLicenseAssertionIssuer>(sp => sp.GetRequiredService<HmacLicenseAssertionService>());

            if (RsaLicenseAssertionValidator.HasKeyMaterial(options))
            {
                // Staging has two legitimate assertion sources — the real license server (RS256)
                // and the in-process test issuer (HMAC) — so both are registered and routed by
                // version prefix. Registering the HMAC arm alone, as this branch used to, made
                // every RS256 assertion from the license server a 401 at the prefix check.
                services.AddSingleton<RsaLicenseAssertionValidator>();
                services.AddSingleton<ILicenseAssertionValidator, PrefixRoutedLicenseAssertionValidator>();
            }
            else
            {
                services.AddSingleton<ILicenseAssertionValidator>(sp => sp.GetRequiredService<HmacLicenseAssertionService>());
            }
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
