using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoJMS.DataHub.Api.Configuration;

namespace AutoJMS.DataHub.Api.Auth;

/// <summary>
/// HMAC implementation used by the staging test issuer and as a small, testable
/// validator seam. Production should replace the validator's key material with
/// the configured license issuer verification mechanism before enrollment is enabled.
/// </summary>
public sealed class HmacLicenseAssertionService : ILicenseAssertionValidator, IStagingTestLicenseAssertionIssuer
{
    private readonly DataHubRuntimeOptions _options;
    private readonly TimeProvider _clock;

    public HmacLicenseAssertionService(DataHubRuntimeOptions options, TimeProvider clock)
    {
        _options = options;
        _clock = clock;
    }

    public string Issue(StagingLicenseAssertionDescriptor descriptor)
    {
        if (!StagingTestIssuerPolicy.IsEnabled(_options.EnvironmentName, _options.AllowStagingTestIssuer))
            throw new InvalidOperationException("The staging test issuer is disabled outside an explicit Staging opt-in.");
        if (!string.Equals(_options.Channel, DataHubRuntimeOptions.AllowedStagingChannel, StringComparison.Ordinal))
            throw new InvalidOperationException("The staging test issuer requires DATAHUB_CHANNEL=staging.");
        var siteCodes = descriptor.SiteCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (siteCodes.Length == 0 || descriptor.ExpiresAt <= _clock.GetUtcNow())
            throw new ArgumentException("A staging assertion requires sites and a future expiry.", nameof(descriptor));

        var payload = new LicenseAssertionPayload
        {
            Channel = DataHubRuntimeOptions.AllowedStagingChannel,
            SiteCodes = siteCodes.Select(NormalizeSiteCode).Distinct(StringComparer.Ordinal).ToArray(),
            ExpiresAt = descriptor.ExpiresAt.ToUnixTimeSeconds(),
            DataHubUrl = descriptor.DataHubUrl,
            Seats = descriptor.Seats,
            TokenVersion = descriptor.TokenVersion,
            Issuer = _options.LicenseAssertionIssuer,
            Audience = _options.LicenseAssertionAudience
        };
        var encodedPayload = Base64Url.Encode(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));
        return $"v1.{encodedPayload}.{Sign(encodedPayload, GetStagingKey())}";
    }

    public ValueTask<LicenseAssertionValidationResult> ValidateAsync(string assertion, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!StagingTestIssuerPolicy.IsEnabled(_options.EnvironmentName, _options.AllowStagingTestIssuer)
            || !string.Equals(_options.Channel, DataHubRuntimeOptions.AllowedStagingChannel, StringComparison.Ordinal))
            return ValueTask.FromResult(LicenseAssertionValidationResult.Failure("LICENSE_ASSERTION_UNAVAILABLE"));
        if (string.IsNullOrWhiteSpace(assertion) || assertion.Length > 8192)
            return ValueTask.FromResult(LicenseAssertionValidationResult.Failure("LICENSE_ASSERTION_MALFORMED"));
        var parts = assertion.Split('.', StringSplitOptions.None);
        if (parts.Length != 3 || parts[0] != "v1" || !Base64Url.TryDecode(parts[1], out var payloadBytes)
            || !Base64Url.TryDecode(parts[2], out var suppliedSignature))
            return ValueTask.FromResult(LicenseAssertionValidationResult.Failure("LICENSE_ASSERTION_MALFORMED"));

        var key = SelectValidationKey();
        if (key.Length < 32 || !CryptographicOperations.FixedTimeEquals(
                HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(parts[1])), suppliedSignature))
            return ValueTask.FromResult(LicenseAssertionValidationResult.Failure("LICENSE_ASSERTION_INVALID"));

        LicenseAssertionPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<LicenseAssertionPayload>(payloadBytes);
        }
        catch (JsonException)
        {
            payload = null;
        }

        // Claim checks live in LicenseAssertionClaims so the RS256 validator enforces the
        // identical rule set — see RsaLicenseAssertionValidator.
        return ValueTask.FromResult(LicenseAssertionClaims.Validate(payload, _options, _clock.GetUtcNow()));
    }

    private byte[] SelectValidationKey()
    {
        if (StagingTestIssuerPolicy.IsEnabled(_options.EnvironmentName, _options.AllowStagingTestIssuer)
            && string.Equals(_options.Channel, DataHubRuntimeOptions.AllowedStagingChannel, StringComparison.Ordinal))
            return Encoding.UTF8.GetBytes(_options.StagingTestSigningKey);
        return Encoding.UTF8.GetBytes(_options.LicenseAssertionValidationKey ?? "");
    }

    private byte[] GetStagingKey()
    {
        var key = Encoding.UTF8.GetBytes(_options.StagingTestSigningKey ?? "");
        if (key.Length < 32) throw new InvalidOperationException("DATAHUB_STAGING_TEST_SIGNING_KEY must be at least 32 bytes.");
        return key;
    }

    private static string Sign(string encodedPayload, byte[] key)
        => Base64Url.Encode(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(encodedPayload)));

    private static string NormalizeSiteCode(string value) => LicenseAssertionClaims.NormalizeSiteCode(value);
}

/// <summary>
/// Production enrollment remains deliberately closed until the real signed
/// license issuer (for example the existing RS256/JWKS service) is integrated.
/// This prevents a staging HMAC test key from becoming a production trust root.
/// </summary>
public sealed class UnavailableLicenseAssertionValidator : ILicenseAssertionValidator
{
    public ValueTask<LicenseAssertionValidationResult> ValidateAsync(string assertion, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(LicenseAssertionValidationResult.Failure("LICENSE_ASSERTION_UNAVAILABLE"));
    }
}
