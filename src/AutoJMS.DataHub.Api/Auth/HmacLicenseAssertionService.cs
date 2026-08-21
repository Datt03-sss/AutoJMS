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
        if (descriptor.SiteCodes.Count == 0 || descriptor.ExpiresAt <= _clock.GetUtcNow())
            throw new ArgumentException("A staging assertion requires sites and a future expiry.", nameof(descriptor));

        var payload = new LicensePayload
        {
            Channel = DataHubRuntimeOptions.AllowedStagingChannel,
            SiteCodes = descriptor.SiteCodes.Where(code => !string.IsNullOrWhiteSpace(code)).Select(code => code.Trim()).Distinct(StringComparer.Ordinal).ToArray(),
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
        var parts = assertion.Split('.', StringSplitOptions.None);
        if (parts.Length != 3 || parts[0] != "v1" || !Base64Url.TryDecode(parts[1], out var payloadBytes)
            || !Base64Url.TryDecode(parts[2], out var suppliedSignature))
            return ValueTask.FromResult(LicenseAssertionValidationResult.Failure("LICENSE_ASSERTION_MALFORMED"));

        var key = SelectValidationKey();
        if (key.Length < 32 || !CryptographicOperations.FixedTimeEquals(
                HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(parts[1])), suppliedSignature))
            return ValueTask.FromResult(LicenseAssertionValidationResult.Failure("LICENSE_ASSERTION_INVALID"));

        LicensePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<LicensePayload>(payloadBytes);
        }
        catch (JsonException)
        {
            payload = null;
        }

        if (payload is null || !DataHubRuntimeOptions.AllowedStagingChannel.Equals(payload.Channel, StringComparison.Ordinal)
            && !DataHubRuntimeOptions.AllowedProductionChannel.Equals(payload.Channel, StringComparison.Ordinal))
            return ValueTask.FromResult(LicenseAssertionValidationResult.Failure("LICENSE_ASSERTION_INVALID"));
        if (!string.Equals(payload.Issuer, _options.LicenseAssertionIssuer, StringComparison.Ordinal)
            || !string.Equals(payload.Audience, _options.LicenseAssertionAudience, StringComparison.Ordinal))
            return ValueTask.FromResult(LicenseAssertionValidationResult.Failure("LICENSE_ASSERTION_INVALID"));
        if (payload.ExpiresAt <= _clock.GetUtcNow().ToUnixTimeSeconds())
            return ValueTask.FromResult(LicenseAssertionValidationResult.Failure("LICENSE_ASSERTION_EXPIRED"));
        if (payload.SiteCodes is null || payload.SiteCodes.Length == 0)
            return ValueTask.FromResult(LicenseAssertionValidationResult.Failure("LICENSE_ASSERTION_INVALID"));
        if (!string.Equals(payload.Channel, _options.Channel, StringComparison.Ordinal))
            return ValueTask.FromResult(LicenseAssertionValidationResult.Failure(ApiProblemCodes.ChannelMismatch));
        if (!string.IsNullOrWhiteSpace(payload.DataHubUrl)
            && (!Uri.TryCreate(payload.DataHubUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
            return ValueTask.FromResult(LicenseAssertionValidationResult.Failure("LICENSE_ASSERTION_INVALID"));

        var identity = new LicenseAssertionIdentity(
            payload.Channel,
            new HashSet<string>(payload.SiteCodes, StringComparer.Ordinal),
            DateTimeOffset.FromUnixTimeSeconds(payload.ExpiresAt),
            payload.DataHubUrl,
            Math.Max(payload.TokenVersion, 1),
            Math.Max(payload.Seats, 1));
        return ValueTask.FromResult(LicenseAssertionValidationResult.Success(identity));
    }

    private byte[] SelectValidationKey()
    {
        if (StagingTestIssuerPolicy.IsEnabled(_options.EnvironmentName, _options.AllowStagingTestIssuer)
            && string.Equals(_options.Channel, DataHubRuntimeOptions.AllowedStagingChannel, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(_options.StagingTestSigningKey))
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

    private sealed class LicensePayload
    {
        public string Channel { get; set; } = "";
        public string[] SiteCodes { get; set; } = [];
        public long ExpiresAt { get; set; }
        public string? DataHubUrl { get; set; }
        public int Seats { get; set; }
        public int TokenVersion { get; set; }
        public string Issuer { get; set; } = "";
        public string Audience { get; set; } = "";
    }
}
