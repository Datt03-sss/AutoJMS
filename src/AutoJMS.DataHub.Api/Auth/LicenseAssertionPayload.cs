using AutoJMS.DataHub.Api.Configuration;

namespace AutoJMS.DataHub.Api.Auth;

/// <summary>
/// The claim set carried by a license assertion, shared by every signature scheme
/// (staging HMAC, production RS256). One definition on purpose: two copies of a wire
/// contract drift, and a drifted claim check is a hole.
/// </summary>
internal sealed class LicenseAssertionPayload
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

/// <summary>
/// Claim validation applied after a signature has already been verified. Kept separate from
/// the signature schemes so both validators enforce exactly the same rules in the same order.
/// </summary>
internal static class LicenseAssertionClaims
{
    public static LicenseAssertionValidationResult Validate(
        LicenseAssertionPayload? payload,
        DataHubRuntimeOptions options,
        DateTimeOffset now)
    {
        if (payload is null || !DataHubRuntimeOptions.AllowedStagingChannel.Equals(payload.Channel, StringComparison.Ordinal)
            && !DataHubRuntimeOptions.AllowedProductionChannel.Equals(payload.Channel, StringComparison.Ordinal))
            return LicenseAssertionValidationResult.Failure("LICENSE_ASSERTION_INVALID");
        if (!string.Equals(payload.Issuer, options.LicenseAssertionIssuer, StringComparison.Ordinal)
            || !string.Equals(payload.Audience, options.LicenseAssertionAudience, StringComparison.Ordinal))
            return LicenseAssertionValidationResult.Failure("LICENSE_ASSERTION_INVALID");
        if (payload.ExpiresAt <= now.ToUnixTimeSeconds())
            return LicenseAssertionValidationResult.Failure("LICENSE_ASSERTION_EXPIRED");

        var normalizedSiteCodes = payload.SiteCodes?
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(NormalizeSiteCode)
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? [];
        if (normalizedSiteCodes.Length == 0)
            return LicenseAssertionValidationResult.Failure("LICENSE_ASSERTION_INVALID");
        if (!string.Equals(payload.Channel, options.Channel, StringComparison.Ordinal))
            return LicenseAssertionValidationResult.Failure(ApiProblemCodes.ChannelMismatch);
        if (!string.IsNullOrWhiteSpace(payload.DataHubUrl)
            && (!Uri.TryCreate(payload.DataHubUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
            return LicenseAssertionValidationResult.Failure("LICENSE_ASSERTION_INVALID");

        return LicenseAssertionValidationResult.Success(new LicenseAssertionIdentity(
            payload.Channel,
            new HashSet<string>(normalizedSiteCodes, StringComparer.Ordinal),
            DateTimeOffset.FromUnixTimeSeconds(payload.ExpiresAt),
            payload.DataHubUrl,
            Math.Max(payload.TokenVersion, 1),
            Math.Max(payload.Seats, 1)));
    }

    public static string NormalizeSiteCode(string value) => value.Trim().ToUpperInvariant();
}
