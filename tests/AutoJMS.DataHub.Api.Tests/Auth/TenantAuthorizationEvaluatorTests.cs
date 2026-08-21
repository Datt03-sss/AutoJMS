using AutoJMS.DataHub.Api.Auth;

namespace AutoJMS.DataHub.Api.Tests.Auth;

public sealed class TenantAuthorizationEvaluatorTests
{
    private static readonly Guid DeviceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SiteId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Evaluate_rejects_a_device_from_another_channel()
    {
        var identity = new DeviceIdentity(DeviceId, SiteId, "staging", "operator", 1);

        var result = TenantAuthorizationEvaluator.Evaluate(identity, SiteId, "production");

        Assert.False(result.Allowed);
        Assert.Equal(ApiProblemCodes.ChannelMismatch, result.ProblemCode);
    }

    [Fact]
    public void Evaluate_rejects_a_route_for_another_site()
    {
        var identity = new DeviceIdentity(DeviceId, SiteId, "production", "operator", 1);

        var result = TenantAuthorizationEvaluator.Evaluate(identity, Guid.NewGuid(), "production");

        Assert.False(result.Allowed);
        Assert.Equal(ApiProblemCodes.SiteNotLicensed, result.ProblemCode);
    }

    [Fact]
    public void Evaluate_accepts_matching_channel_and_site()
    {
        var identity = new DeviceIdentity(DeviceId, SiteId, "production", "operator", 1);

        var result = TenantAuthorizationEvaluator.Evaluate(identity, SiteId, "production");

        Assert.True(result.Allowed);
        Assert.Null(result.ProblemCode);
    }
}
