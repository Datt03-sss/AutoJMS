namespace AutoJMS.DataHub.Api.Auth;

/// <summary>
/// Server-side record of *why* a license assertion was refused.
///
/// The enrollment endpoint answers an unauthenticated caller, so it deliberately returns one
/// generic detail string for MALFORMED / INVALID / EXPIRED alike — discriminating there would
/// hand an attacker an oracle for probing assertions. That left operators with a bare 401 and
/// nothing to act on: a staging host quietly running the HMAC validator rejected every RS256
/// assertion at the prefix check, and the only visible symptom was "401 UNAUTHORIZED".
///
/// These lines close that gap on the host's own stderr, where only an operator can read them.
/// Console.Error rather than ILogger on purpose: the validators are constructed from a bare
/// ServiceCollection in tests that never call AddLogging, and it matches the existing
/// key-material diagnostics in <see cref="RsaLicenseAssertionValidator"/>.
/// </summary>
internal static class LicenseAssertionDiagnostics
{
    /// <param name="scheme">Which validator refused — "RS256" or "staging HMAC".</param>
    /// <param name="reason">
    /// What failed. Must describe the check, never the assertion: no payload, no signature, no
    /// key material ever goes through here.
    /// </param>
    public static void Refused(string scheme, string reason)
        => Console.Error.WriteLine($"[DataHub] License assertion refused ({scheme}): {reason}");
}
