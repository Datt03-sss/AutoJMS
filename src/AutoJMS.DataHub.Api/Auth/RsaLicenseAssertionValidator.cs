using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoJMS.DataHub.Api.Configuration;

namespace AutoJMS.DataHub.Api.Auth;

/// <summary>
/// Production license verifier. Assertions are <c>v1rs256.&lt;payload&gt;.&lt;signature&gt;</c>, where
/// the payload is the Base64Url JSON of <see cref="LicenseAssertionPayload"/> and the signature is
/// RSASSA-PKCS1-v1_5 over SHA-256 of the encoded payload bytes.
///
/// Only a PUBLIC key lives on the DataHub host: the private signing key stays with the license
/// issuer, so a compromised VPS cannot mint licenses. The version prefix is distinct from the
/// staging HMAC's <c>v1</c> so a staging assertion can never be replayed against this validator.
///
/// With no key material configured this type is not registered at all and enrollment stays
/// closed by <see cref="UnavailableLicenseAssertionValidator"/> — never open.
/// </summary>
public sealed class RsaLicenseAssertionValidator : ILicenseAssertionValidator, IDisposable
{
    public const string VersionPrefix = "v1rs256";
    private const int MinimumKeySizeBits = 2048;

    private readonly DataHubRuntimeOptions _options;
    private readonly TimeProvider _clock;
    private readonly RSA? _publicKey;

    public RsaLicenseAssertionValidator(DataHubRuntimeOptions options, TimeProvider clock)
    {
        _options = options;
        _clock = clock;
        _publicKey = TryImportPublicKey(ResolvePublicKeyPem(options), out var failure);
        if (_publicKey is null && !string.IsNullOrEmpty(failure))
            Console.Error.WriteLine($"[DataHub] License assertion public key unusable: {failure}. Enrollment stays closed.");
    }

    /// <summary>True when the deployment supplied license key material to verify against.</summary>
    public static bool HasKeyMaterial(DataHubRuntimeOptions options)
        => !string.IsNullOrWhiteSpace(ResolvePublicKeyPem(options));

    public ValueTask<LicenseAssertionValidationResult> ValidateAsync(string assertion, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Bad or missing key material must fail closed, never open.
        if (_publicKey is null)
            return ValueTask.FromResult(LicenseAssertionValidationResult.Failure("LICENSE_ASSERTION_UNAVAILABLE"));
        if (string.IsNullOrWhiteSpace(assertion) || assertion.Length > 8192)
        {
            LicenseAssertionDiagnostics.Refused("RS256", "Assertion is empty or over the 8192-character limit");
            return ValueTask.FromResult(LicenseAssertionValidationResult.Failure("LICENSE_ASSERTION_MALFORMED"));
        }

        var parts = assertion.Split('.', StringSplitOptions.None);
        if (parts.Length != 3)
        {
            LicenseAssertionDiagnostics.Refused("RS256", $"Expected 3 dot-separated segments, got {parts.Length}");
            return ValueTask.FromResult(LicenseAssertionValidationResult.Failure("LICENSE_ASSERTION_MALFORMED"));
        }
        if (!string.Equals(parts[0], VersionPrefix, StringComparison.Ordinal))
        {
            LicenseAssertionDiagnostics.Refused("RS256", $"Prefix mismatch: expected '{VersionPrefix}' got '{parts[0]}'");
            return ValueTask.FromResult(LicenseAssertionValidationResult.Failure("LICENSE_ASSERTION_MALFORMED"));
        }
        if (!Base64Url.TryDecode(parts[1], out var payloadBytes) || !Base64Url.TryDecode(parts[2], out var signature))
        {
            LicenseAssertionDiagnostics.Refused("RS256", "Payload or signature segment is not valid base64url");
            return ValueTask.FromResult(LicenseAssertionValidationResult.Failure("LICENSE_ASSERTION_MALFORMED"));
        }

        bool signatureValid;
        try
        {
            signatureValid = _publicKey.VerifyData(
                Encoding.UTF8.GetBytes(parts[1]),
                signature,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
        }
        catch (CryptographicException)
        {
            signatureValid = false;
        }

        if (!signatureValid)
        {
            LicenseAssertionDiagnostics.Refused("RS256",
                "RS256 signature verification failed — the assertion was signed by a different key than DATAHUB_LICENSE_ASSERTION_PUBLIC_KEY");
            return ValueTask.FromResult(LicenseAssertionValidationResult.Failure("LICENSE_ASSERTION_INVALID"));
        }

        LicenseAssertionPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<LicenseAssertionPayload>(payloadBytes);
        }
        catch (JsonException)
        {
            payload = null;
        }

        var result = LicenseAssertionClaims.Validate(payload, _options, _clock.GetUtcNow(), out var diagnostic);
        if (!result.Succeeded) LicenseAssertionDiagnostics.Refused("RS256", diagnostic);
        return ValueTask.FromResult(result);
    }

    public void Dispose() => _publicKey?.Dispose();

    private static string ResolvePublicKeyPem(DataHubRuntimeOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.LicenseAssertionPublicKeyPath))
        {
            try
            {
                return File.ReadAllText(options.LicenseAssertionPublicKeyPath.Trim());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                Console.Error.WriteLine($"[DataHub] Cannot read DATAHUB_LICENSE_ASSERTION_PUBLIC_KEY_PATH: {ex.Message}");
                return "";
            }
        }

        // Env vars cannot carry real newlines everywhere (Compose, systemd), so accept \n escapes.
        return (options.LicenseAssertionPublicKeyPem ?? "").Replace("\\n", "\n");
    }

    private static RSA? TryImportPublicKey(string pem, out string failure)
    {
        failure = "";
        if (string.IsNullOrWhiteSpace(pem)) return null;

        var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(pem);
            if (rsa.KeySize < MinimumKeySizeBits)
            {
                failure = $"key is {rsa.KeySize} bits, minimum is {MinimumKeySizeBits}";
                rsa.Dispose();
                return null;
            }

            // Refuse a private key: the signing key must never be deployed to the DataHub host,
            // and accepting one here would quietly turn the API into a license issuer.
            try
            {
                rsa.ExportParameters(includePrivateParameters: true);
                failure = "a PRIVATE key was supplied; configure the license issuer's PUBLIC key only";
                rsa.Dispose();
                return null;
            }
            catch (CryptographicException)
            {
                // Expected for a public-only key.
            }

            return rsa;
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException)
        {
            failure = ex.Message;
            rsa.Dispose();
            return null;
        }
    }
}
