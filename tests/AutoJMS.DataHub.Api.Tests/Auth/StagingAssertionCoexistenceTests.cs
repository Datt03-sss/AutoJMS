using System.Security.Cryptography;
using AutoJMS.DataHub.Api.Auth;
using AutoJMS.DataHub.Api.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AutoJMS.DataHub.Api.Tests.Auth;

/// <summary>
/// Staging chạy được ĐỒNG THỜI hai nguồn assertion: license server thật (RS256, prefix
/// <c>v1rs256.</c>) và test issuer nội bộ (HMAC, prefix <c>v1.</c>).
///
/// Trước đây <see cref="IdentityServiceCollectionExtensions.AddDataHubIdentity"/> đăng ký
/// HMAC và bỏ hẳn RSA khi staging opt-in bật, nên mọi assertion RS256 chết ngay ở bước so
/// prefix (HmacLicenseAssertionService.cs:62) và trả 401 UNAUTHORIZED — đúng triệu chứng
/// đo được trên dev.jmsauto.online ngày 2026-09-11.
///
/// Cho hai validator sống chung KHÔNG nới lỏng gì: hai prefix cố ý rời nhau
/// (RsaLicenseAssertionValidator.cs:14-15) nên không assertion nào chạy được vào nhầm
/// validator, và mỗi validator vẫn tự chạy trọn bộ kiểm tra claim của nó.
/// backend/datahub/scripts/smoke-test.sh:339-340 đã ghi sẵn hợp đồng này: "the API accepts
/// only v1. (staging issuer) or v1rs256. (RSA)".
/// </summary>
public sealed class StagingAssertionCoexistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 2, 0, 0, TimeSpan.Zero);
    private const string Issuer = "autojms-license-staging";
    private const string Audience = "autojms-datahub-enroll-staging";

    [Fact]
    public async Task Staging_co_public_key_thi_chap_nhan_assertion_RS256_cua_license_server()
    {
        using var issuerKey = RSA.Create(2048);
        var validator = BuildValidator(StagingOptions(issuerKey), out _);

        var result = await validator.ValidateAsync(
            RsaAssertion(issuerKey, "staging", "214A03"), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("staging", result.Identity!.Channel);
        Assert.Contains("214A03", result.Identity.SiteCodes);
    }

    [Fact]
    public async Task Staging_co_public_key_van_giu_duoc_assertion_HMAC_cua_test_issuer()
    {
        using var issuerKey = RSA.Create(2048);
        var validator = BuildValidator(StagingOptions(issuerKey), out var provider);

        var issued = provider.GetRequiredService<IStagingTestLicenseAssertionIssuer>()
            .Issue(new StagingLicenseAssertionDescriptor(["214A03"], Now.AddHours(1), null, 3, 1));
        var result = await validator.ValidateAsync(issued, CancellationToken.None);

        Assert.StartsWith("v1.", issued);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Staging_khong_co_public_key_thi_hanh_vi_cu_giu_nguyen()
    {
        var options = StagingOptions(issuerKey: null);
        var validator = BuildValidator(options, out var provider);

        var issued = provider.GetRequiredService<IStagingTestLicenseAssertionIssuer>()
            .Issue(new StagingLicenseAssertionDescriptor(["214A03"], Now.AddHours(1), null, 3, 1));

        Assert.True((await validator.ValidateAsync(issued, CancellationToken.None)).Succeeded);
        Assert.False((await validator.ValidateAsync("v1rs256.AAAA.BBBB", CancellationToken.None)).Succeeded);
    }

    /// <summary>
    /// Ranh giới production không được suy suyển: có public key thì assertion HMAC vẫn phải
    /// bị từ chối, nếu không một khoá test staging sẽ thành trust root của production.
    /// </summary>
    [Fact]
    public async Task Production_khong_bao_gio_chap_nhan_HMAC_du_da_co_public_key()
    {
        using var issuerKey = RSA.Create(2048);
        var options = StagingOptions(issuerKey);
        options.EnvironmentName = "Production";
        options.Channel = "production";
        var validator = BuildValidator(options, out var provider);

        var result = await validator.ValidateAsync("v1.AAAA.BBBB", CancellationToken.None);

        Assert.Null(provider.GetService<IStagingTestLicenseAssertionIssuer>());
        Assert.False(result.Succeeded);
        Assert.True(
            (await validator.ValidateAsync(RsaAssertion(issuerKey, "production", "214A03"), CancellationToken.None))
            .Succeeded);
    }

    private static ILicenseAssertionValidator BuildValidator(
        DataHubRuntimeOptions options, out ServiceProvider provider)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        services.AddDataHubIdentity(options);
        provider = services.BuildServiceProvider();
        return provider.GetRequiredService<ILicenseAssertionValidator>();
    }

    private static DataHubRuntimeOptions StagingOptions(RSA? issuerKey) => new()
    {
        EnvironmentName = "Staging",
        Channel = "staging",
        AllowStagingTestIssuer = true,
        StagingTestSigningKey = new string('s', 32),
        DeviceTokenSigningKey = new string('d', 32),
        LicenseAssertionIssuer = Issuer,
        LicenseAssertionAudience = Audience,
        LicenseAssertionPublicKeyPem = issuerKey?.ExportSubjectPublicKeyInfoPem() ?? ""
    };

    private static string RsaAssertion(RSA key, string channel, string siteCode)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            Channel = channel,
            SiteCodes = new[] { siteCode },
            ExpiresAt = Now.AddHours(1).ToUnixTimeSeconds(),
            DataHubUrl = (string?)null,
            Seats = 3,
            TokenVersion = 1,
            Issuer = Issuer,
            Audience = Audience
        });
        var encoded = Base64UrlEncode(System.Text.Encoding.UTF8.GetBytes(json));
        var signature = key.SignData(
            System.Text.Encoding.UTF8.GetBytes(encoded), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{RsaLicenseAssertionValidator.VersionPrefix}.{encoded}.{Base64UrlEncode(signature)}";
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
