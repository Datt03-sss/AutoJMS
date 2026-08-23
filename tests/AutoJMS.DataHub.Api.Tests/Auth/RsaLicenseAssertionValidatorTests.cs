using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoJMS.DataHub.Api.Auth;
using AutoJMS.DataHub.Api.Configuration;

namespace AutoJMS.DataHub.Api.Tests.Auth;

public sealed class RsaLicenseAssertionValidatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Accepts_an_assertion_signed_by_the_configured_issuer_key()
    {
        using var issuerKey = RSA.Create(2048);
        var validator = CreateValidator(issuerKey, out var options);

        var assertion = Sign(issuerKey, Payload(options, Now.AddDays(30), ["272C03"]));
        var result = await validator.ValidateAsync(assertion, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("production", result.Identity!.Channel);
        Assert.Contains("272C03", result.Identity.SiteCodes);
    }

    [Fact]
    public async Task Rejects_an_assertion_signed_by_a_different_key()
    {
        using var issuerKey = RSA.Create(2048);
        using var attackerKey = RSA.Create(2048);
        var validator = CreateValidator(issuerKey, out var options);

        var forged = Sign(attackerKey, Payload(options, Now.AddDays(30), ["272C03"]));
        var result = await validator.ValidateAsync(forged, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("LICENSE_ASSERTION_INVALID", result.FailureCode);
    }

    [Fact]
    public async Task Rejects_a_staging_hmac_assertion_because_the_version_prefix_differs()
    {
        using var issuerKey = RSA.Create(2048);
        var validator = CreateValidator(issuerKey, out var options);

        var payload = Base64UrlEncode(Encoding.UTF8.GetBytes(Payload(options, Now.AddDays(30), ["272C03"])));
        var hmacShaped = $"v1.{payload}.{Base64UrlEncode(new byte[32])}";
        var result = await validator.ValidateAsync(hmacShaped, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("LICENSE_ASSERTION_MALFORMED", result.FailureCode);
    }

    [Fact]
    public async Task Rejects_an_expired_assertion()
    {
        using var issuerKey = RSA.Create(2048);
        var validator = CreateValidator(issuerKey, out var options);

        var assertion = Sign(issuerKey, Payload(options, Now.AddMinutes(-1), ["272C03"]));
        var result = await validator.ValidateAsync(assertion, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("LICENSE_ASSERTION_EXPIRED", result.FailureCode);
    }

    [Fact]
    public async Task Rejects_an_assertion_for_another_channel()
    {
        using var issuerKey = RSA.Create(2048);
        var validator = CreateValidator(issuerKey, out var options);

        var assertion = Sign(issuerKey, Payload(options, Now.AddDays(30), ["272C03"], channel: "staging"));
        var result = await validator.ValidateAsync(assertion, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ApiProblemCodes.ChannelMismatch, result.FailureCode);
    }

    [Fact]
    public async Task Fails_closed_when_the_configured_key_is_a_private_key()
    {
        using var issuerKey = RSA.Create(2048);
        var options = BaseOptions();
        options.LicenseAssertionPublicKeyPem = issuerKey.ExportPkcs8PrivateKeyPem();
        using var validator = new RsaLicenseAssertionValidator(options, new FixedTimeProvider(Now));

        var assertion = Sign(issuerKey, Payload(options, Now.AddDays(30), ["272C03"]));
        var result = await validator.ValidateAsync(assertion, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("LICENSE_ASSERTION_UNAVAILABLE", result.FailureCode);
    }

    [Fact]
    public async Task Fails_closed_when_no_key_material_is_configured()
    {
        var options = BaseOptions();
        using var validator = new RsaLicenseAssertionValidator(options, new FixedTimeProvider(Now));

        Assert.False(RsaLicenseAssertionValidator.HasKeyMaterial(options));
        var result = await validator.ValidateAsync("v1rs256.payload.signature", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("LICENSE_ASSERTION_UNAVAILABLE", result.FailureCode);
    }

    [Fact]
    public async Task Accepts_a_pem_supplied_with_escaped_newlines()
    {
        using var issuerKey = RSA.Create(2048);
        var options = BaseOptions();
        options.LicenseAssertionPublicKeyPem = issuerKey.ExportSubjectPublicKeyInfoPem()
            .Replace("\r\n", "\n")
            .Replace("\n", "\\n");
        using var validator = new RsaLicenseAssertionValidator(options, new FixedTimeProvider(Now));

        var result = await validator.ValidateAsync(
            Sign(issuerKey, Payload(options, Now.AddDays(30), ["272C03"])), CancellationToken.None);

        Assert.True(result.Succeeded);
    }

    private static RsaLicenseAssertionValidator CreateValidator(RSA issuerKey, out DataHubRuntimeOptions options)
    {
        options = BaseOptions();
        options.LicenseAssertionPublicKeyPem = issuerKey.ExportSubjectPublicKeyInfoPem();
        Assert.True(RsaLicenseAssertionValidator.HasKeyMaterial(options));
        return new RsaLicenseAssertionValidator(options, new FixedTimeProvider(Now));
    }

    private static DataHubRuntimeOptions BaseOptions() => new()
    {
        Channel = "production",
        EnvironmentName = "Production",
        LicenseAssertionIssuer = "autojms-license",
        LicenseAssertionAudience = "autojms-datahub-enroll"
    };

    private static string Payload(
        DataHubRuntimeOptions options,
        DateTimeOffset expiresAt,
        string[] siteCodes,
        string? channel = null) => JsonSerializer.Serialize(new
        {
            Channel = channel ?? options.Channel,
            SiteCodes = siteCodes,
            ExpiresAt = expiresAt.ToUnixTimeSeconds(),
            DataHubUrl = (string?)null,
            Seats = 3,
            TokenVersion = 1,
            Issuer = options.LicenseAssertionIssuer,
            Audience = options.LicenseAssertionAudience
        });

    private static string Sign(RSA key, string payloadJson)
    {
        var encodedPayload = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));
        var signature = key.SignData(
            Encoding.UTF8.GetBytes(encodedPayload), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{RsaLicenseAssertionValidator.VersionPrefix}.{encodedPayload}.{Base64UrlEncode(signature)}";
    }

    // Deliberately a local copy rather than the API's internal Base64Url helper: the test then
    // verifies the encoding is really interoperable instead of sharing the implementation.
    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
