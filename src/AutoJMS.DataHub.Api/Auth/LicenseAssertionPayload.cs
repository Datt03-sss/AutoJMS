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
        => Validate(payload, options, now, out _);

    /// <param name="diagnostic">
    /// Which check refused, in operator-readable form, or "" on success. Five distinct causes
    /// collapse into a single LICENSE_ASSERTION_INVALID on the wire — deliberately, because the
    /// caller is unauthenticated and a discriminating response would be an oracle — so this is
    /// the only place the difference survives. Log it; never return it.
    /// </param>
    public static LicenseAssertionValidationResult Validate(
        LicenseAssertionPayload? payload,
        DataHubRuntimeOptions options,
        DateTimeOffset now,
        out string diagnostic)
    {
        diagnostic = "";
        if (payload is null)
        {
            diagnostic = "Payload is not valid JSON for a license assertion";
            return LicenseAssertionValidationResult.Failure("LICENSE_ASSERTION_INVALID");
        }
        if (!DataHubRuntimeOptions.AllowedStagingChannel.Equals(payload.Channel, StringComparison.Ordinal)
            && !DataHubRuntimeOptions.AllowedProductionChannel.Equals(payload.Channel, StringComparison.Ordinal))
        {
            diagnostic = $"Channel is not a known value: got '{payload.Channel}'";
            return LicenseAssertionValidationResult.Failure("LICENSE_ASSERTION_INVALID");
        }
        if (!string.Equals(payload.Issuer, options.LicenseAssertionIssuer, StringComparison.Ordinal))
        {
            diagnostic = $"Issuer mismatch: expected '{options.LicenseAssertionIssuer}' got '{payload.Issuer}'";
            return LicenseAssertionValidationResult.Failure("LICENSE_ASSERTION_INVALID");
        }
        if (!string.Equals(payload.Audience, options.LicenseAssertionAudience, StringComparison.Ordinal))
        {
            diagnostic = $"Audience mismatch: expected '{options.LicenseAssertionAudience}' got '{payload.Audience}'";
            return LicenseAssertionValidationResult.Failure("LICENSE_ASSERTION_INVALID");
        }
        if (payload.ExpiresAt <= now.ToUnixTimeSeconds())
        {
            diagnostic = $"Expired at {DateTimeOffset.FromUnixTimeSeconds(payload.ExpiresAt):u}, now {now:u}";
            return LicenseAssertionValidationResult.Failure("LICENSE_ASSERTION_EXPIRED");
        }

        var normalizedSiteCodes = payload.SiteCodes?
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(NormalizeSiteCode)
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? [];
        if (normalizedSiteCodes.Length == 0)
        {
            diagnostic = "Assertion carries no site codes";
            return LicenseAssertionValidationResult.Failure("LICENSE_ASSERTION_INVALID");
        }
        if (!string.Equals(payload.Channel, options.Channel, StringComparison.Ordinal))
        {
            diagnostic = $"Channel mismatch: expected '{options.Channel}' got '{payload.Channel}'";
            return LicenseAssertionValidationResult.Failure(ApiProblemCodes.ChannelMismatch);
        }
        if (!string.IsNullOrWhiteSpace(payload.DataHubUrl)
            && (!Uri.TryCreate(payload.DataHubUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
        {
            diagnostic = "DataHubUrl is not an absolute https URL";
            return LicenseAssertionValidationResult.Failure("LICENSE_ASSERTION_INVALID");
        }

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
