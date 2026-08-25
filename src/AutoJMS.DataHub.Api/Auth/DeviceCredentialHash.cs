using System.Security.Cryptography;
using System.Text;

namespace AutoJMS.DataHub.Api.Auth;

/// <summary>
/// The single definition of <c>devices.credential_hash</c>.
///
/// Enrollment writes this digest; authentication recomputes it from the presented
/// bearer token and requires a match. That makes the row a genuine second factor:
/// even with the device-token signing key, a forged token for a known
/// (device_id, site_id, token_version) tuple hashes to a different value and is
/// rejected — provided the enrollment pepper stays a separate secret from the
/// signing key, which is why they are separate environment variables.
///
/// It lives here rather than inline at both call sites because the two must agree
/// byte for byte forever: any drift in the encoding, the digest, or the hex casing
/// would not fail a build or a unit test in isolation, it would 401 every enrolled
/// device in the fleet at once.
/// </summary>
public static class DeviceCredentialHash
{
    /// <summary>
    /// HMAC-SHA256 of the issued bearer token under the enrollment pepper, as
    /// lowercase hex. Never log the result: it is a verifier for a live credential.
    /// </summary>
    public static string Compute(string enrollmentPepper, string deviceToken)
        => Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(enrollmentPepper),
            Encoding.UTF8.GetBytes(deviceToken))).ToLowerInvariant();
}
