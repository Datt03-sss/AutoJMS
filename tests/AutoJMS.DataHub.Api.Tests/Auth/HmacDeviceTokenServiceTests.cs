using AutoJMS.DataHub.Api.Auth;
using AutoJMS.DataHub.Api.Configuration;

namespace AutoJMS.DataHub.Api.Tests.Auth;

public sealed class HmacDeviceTokenServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Issued_token_validates_to_the_bound_device_identity()
    {
        var clock = new FixedTimeProvider(Now);
        var service = new HmacDeviceTokenService(CreateOptions(), clock);
        var descriptor = new DeviceTokenDescriptor(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "production",
            "operator",
            3,
            Now.AddMinutes(10));

        var token = service.Issue(descriptor);
        var result = await service.ValidateAsync(token, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(descriptor.DeviceId, result.Identity!.DeviceId);
        Assert.Equal(descriptor.SiteId, result.Identity.SiteId);
        Assert.Equal("production", result.Identity.Channel);
        Assert.Equal(3, result.Identity.TokenVersion);
    }

    [Fact]
    public async Task Validation_rejects_a_tampered_token()
    {
        var service = new HmacDeviceTokenService(CreateOptions(), new FixedTimeProvider(Now));
        var token = service.Issue(new DeviceTokenDescriptor(
            Guid.NewGuid(), Guid.NewGuid(), "production", "operator", 1, Now.AddMinutes(10)));
        var parts = token.Split('.');
        parts[1] = (parts[1][0] == 'a' ? 'b' : 'a') + parts[1][1..];
        var tampered = string.Join('.', parts);

        var result = await service.ValidateAsync(tampered, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.Identity);
    }

    [Fact]
    public async Task Validation_rejects_an_expired_token()
    {
        var service = new HmacDeviceTokenService(CreateOptions(), new FixedTimeProvider(Now));
        var token = service.Issue(new DeviceTokenDescriptor(
            Guid.NewGuid(), Guid.NewGuid(), "production", "operator", 1, Now.AddSeconds(-1)));

        var result = await service.ValidateAsync(token, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("TOKEN_EXPIRED", result.FailureCode);
    }

    private static DataHubRuntimeOptions CreateOptions() => new()
    {
        Channel = "production",
        DeviceTokenIssuer = "autojms-datahub",
        DeviceTokenAudience = "autojms-device",
        DeviceTokenSigningKey = "device-token-signing-key-with-at-least-32-bytes"
    };

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
