using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoJMS.DataHub.Api.Configuration;

namespace AutoJMS.DataHub.Api.Auth;

public sealed class HmacDeviceTokenService : IDeviceTokenService
{
    private readonly DataHubRuntimeOptions _options;
    private readonly TimeProvider _clock;
    private readonly byte[] _key;

    public HmacDeviceTokenService(DataHubRuntimeOptions options, TimeProvider clock)
    {
        _options = options;
        _clock = clock;
        _key = Encoding.UTF8.GetBytes(options.DeviceTokenSigningKey ?? "");
    }

    public string Issue(DeviceTokenDescriptor descriptor)
    {
        if (_key.Length < 32) throw new InvalidOperationException("DATAHUB_DEVICE_TOKEN_SIGNING_KEY must be at least 32 bytes.");
        if (descriptor.TokenVersion < 1) throw new ArgumentOutOfRangeException(nameof(descriptor));

        var payload = new DeviceTokenPayload
        {
            DeviceId = descriptor.DeviceId,
            SiteId = descriptor.SiteId,
            Channel = descriptor.Channel,
            Role = descriptor.Role,
            TokenVersion = descriptor.TokenVersion,
            ExpiresAt = descriptor.ExpiresAt.ToUnixTimeSeconds(),
            Issuer = _options.DeviceTokenIssuer,
            Audience = _options.DeviceTokenAudience
        };
        var encodedPayload = Base64Url.Encode(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));
        return $"v1.{encodedPayload}.{Sign(encodedPayload)}";
    }

    public ValueTask<DeviceTokenValidationResult> ValidateAsync(string token, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var parts = token.Split('.', StringSplitOptions.None);
        if (parts.Length != 3 || parts[0] != "v1" || !Base64Url.TryDecode(parts[1], out var payloadBytes)
            || !Base64Url.TryDecode(parts[2], out var suppliedSignature))
            return ValueTask.FromResult(DeviceTokenValidationResult.Failure("TOKEN_MALFORMED"));

        var expectedSignature = ComputeSignature(parts[1]);
        if (!CryptographicOperations.FixedTimeEquals(expectedSignature, suppliedSignature))
            return ValueTask.FromResult(DeviceTokenValidationResult.Failure("TOKEN_INVALID"));

        DeviceTokenPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<DeviceTokenPayload>(payloadBytes);
        }
        catch (JsonException)
        {
            payload = null;
        }

        if (payload is null || payload.DeviceId == Guid.Empty || payload.SiteId == Guid.Empty
            || string.IsNullOrWhiteSpace(payload.Channel) || string.IsNullOrWhiteSpace(payload.Role)
            || payload.TokenVersion < 1)
            return ValueTask.FromResult(DeviceTokenValidationResult.Failure("TOKEN_INVALID"));

        if (!string.Equals(payload.Issuer, _options.DeviceTokenIssuer, StringComparison.Ordinal)
            || !string.Equals(payload.Audience, _options.DeviceTokenAudience, StringComparison.Ordinal))
            return ValueTask.FromResult(DeviceTokenValidationResult.Failure("TOKEN_INVALID"));

        if (payload.ExpiresAt <= _clock.GetUtcNow().ToUnixTimeSeconds())
            return ValueTask.FromResult(DeviceTokenValidationResult.Failure("TOKEN_EXPIRED"));

        return ValueTask.FromResult(DeviceTokenValidationResult.Success(new DeviceIdentity(
            payload.DeviceId,
            payload.SiteId,
            payload.Channel,
            payload.Role,
            payload.TokenVersion)));
    }

    private string Sign(string encodedPayload)
        => Base64Url.Encode(ComputeSignature(encodedPayload));

    private byte[] ComputeSignature(string encodedPayload)
        => HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(encodedPayload));

    private sealed class DeviceTokenPayload
    {
        public Guid DeviceId { get; set; }
        public Guid SiteId { get; set; }
        public string Channel { get; set; } = "";
        public string Role { get; set; } = "";
        public int TokenVersion { get; set; }
        public long ExpiresAt { get; set; }
        public string Issuer { get; set; } = "";
        public string Audience { get; set; } = "";
    }
}
