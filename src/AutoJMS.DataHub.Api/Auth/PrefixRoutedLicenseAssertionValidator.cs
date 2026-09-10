namespace AutoJMS.DataHub.Api.Auth;

/// <summary>
/// Routes an assertion to the validator that owns its version prefix: <c>v1rs256.</c> to the
/// RS256 verifier, <c>v1.</c> to the staging test issuer's HMAC verifier.
///
/// Staging genuinely has two assertion sources — the real license server, which mints RS256, and
/// the in-process test issuer, which mints HMAC — and the enrollment endpoint's contract already
/// says so (backend/datahub/scripts/smoke-test.sh). Registering only one of them meant every
/// RS256 assertion died at the other validator's prefix check and came back 401.
///
/// Routing is not a widening. The two prefixes are disjoint by design, so no assertion can reach
/// the validator it was not signed for, and each validator still runs its own full claim check
/// (issuer, audience, channel, expiry, sites) after verifying its own signature. This type is
/// only ever registered when BOTH arms are legitimately available: staging opt-in for the HMAC
/// arm, configured public key material for the RS256 arm.
/// </summary>
internal sealed class PrefixRoutedLicenseAssertionValidator : ILicenseAssertionValidator
{
    private readonly ILicenseAssertionValidator _rsa;
    private readonly ILicenseAssertionValidator _stagingHmac;

    public PrefixRoutedLicenseAssertionValidator(
        RsaLicenseAssertionValidator rsa, HmacLicenseAssertionService stagingHmac)
    {
        _rsa = rsa;
        _stagingHmac = stagingHmac;
    }

    public ValueTask<LicenseAssertionValidationResult> ValidateAsync(string assertion, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var prefix = ReadPrefix(assertion);
        if (string.Equals(prefix, RsaLicenseAssertionValidator.VersionPrefix, StringComparison.Ordinal))
            return _rsa.ValidateAsync(assertion, cancellationToken);
        if (string.Equals(prefix, HmacLicenseAssertionService.VersionPrefix, StringComparison.Ordinal))
            return _stagingHmac.ValidateAsync(assertion, cancellationToken);

        LicenseAssertionDiagnostics.Refused("prefix router",
            $"Unknown version prefix '{prefix}'; expected '{RsaLicenseAssertionValidator.VersionPrefix}' or '{HmacLicenseAssertionService.VersionPrefix}'");
        return ValueTask.FromResult(LicenseAssertionValidationResult.Failure("LICENSE_ASSERTION_MALFORMED"));
    }

    /// <summary>
    /// The segment before the first dot, bounded so an oversized body is not scanned or echoed.
    /// Both arms re-check the prefix themselves; this only decides who gets asked.
    /// </summary>
    private static string ReadPrefix(string assertion)
    {
        if (string.IsNullOrEmpty(assertion)) return "";
        var dot = assertion.IndexOf('.');
        if (dot <= 0 || dot > 16) return "";
        return assertion[..dot];
    }
}
