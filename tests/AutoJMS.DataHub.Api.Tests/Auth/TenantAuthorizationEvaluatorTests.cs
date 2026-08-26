using AutoJMS.DataHub.Api.Auth;

namespace AutoJMS.DataHub.Api.Tests.Auth;

public sealed class TenantAuthorizationEvaluatorTests
{
    private static readonly Guid DeviceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SiteId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Evaluate_rejects_a_device_from_another_channel()
    {
        var identity = new DeviceIdentity(DeviceId, SiteId, "staging", DeviceRoles.Operator, 1);

        var result = TenantAuthorizationEvaluator.Evaluate(identity, SiteId, "production", DeviceCapability.ReadSiteData);

        Assert.False(result.Allowed);
        Assert.Equal(ApiProblemCodes.ChannelMismatch, result.ProblemCode);
    }

    [Fact]
    public void Evaluate_rejects_a_route_for_another_site()
    {
        var identity = new DeviceIdentity(DeviceId, SiteId, "production", DeviceRoles.Operator, 1);

        var result = TenantAuthorizationEvaluator.Evaluate(identity, Guid.NewGuid(), "production", DeviceCapability.ReadSiteData);

        Assert.False(result.Allowed);
        Assert.Equal(ApiProblemCodes.SiteNotLicensed, result.ProblemCode);
    }

    [Fact]
    public void Evaluate_accepts_matching_channel_and_site()
    {
        var identity = new DeviceIdentity(DeviceId, SiteId, "production", DeviceRoles.Operator, 1);

        var result = TenantAuthorizationEvaluator.Evaluate(identity, SiteId, "production", DeviceCapability.ReadSiteData);

        Assert.True(result.Allowed);
        Assert.Null(result.ProblemCode);
    }

    [Theory]
    [InlineData(DeviceCapability.ReadSiteData)]
    [InlineData(DeviceCapability.WriteSiteData)]
    public void Evaluate_rejects_a_role_this_build_does_not_recognise(DeviceCapability capability)
    {
        // "viewer" is not in DeviceRoles.All. A token can only carry it if it was signed by a
        // build with a different role vocabulary, or assembled by hand — either way this
        // deployment has no policy for it and must refuse rather than assume the lesser right.
        var identity = new DeviceIdentity(DeviceId, SiteId, "production", "viewer", 1);

        var result = TenantAuthorizationEvaluator.Evaluate(identity, SiteId, "production", capability);

        Assert.False(result.Allowed);
        Assert.Equal(ApiProblemCodes.Forbidden, result.ProblemCode);
    }

    [Fact]
    public void Evaluate_reports_the_site_mismatch_ahead_of_the_role()
    {
        // Ordering matters for the operator reading the log: a device asking about somebody
        // else's site should be told that, not handed a role complaint that suggests its own
        // enrollment is broken.
        var identity = new DeviceIdentity(DeviceId, SiteId, "production", "viewer", 1);

        var result = TenantAuthorizationEvaluator.Evaluate(identity, Guid.NewGuid(), "production", DeviceCapability.WriteSiteData);

        Assert.Equal(ApiProblemCodes.SiteNotLicensed, result.ProblemCode);
    }

    [Theory]
    [InlineData(DeviceRoles.Operator)]
    [InlineData(DeviceRoles.Leader)]
    [InlineData(DeviceRoles.Admin)]
    public void Every_known_role_may_read_and_write_its_own_site(string role)
    {
        // Pinning today's policy, not asserting it is the only defensible one: enrollment issues
        // `operator` exclusively, so a rule that granted writes only to `leader`/`admin` would
        // lock out the entire fleet. What DeviceRolePolicy actually closes is the unlisted-role
        // hole above; if this deployment ever narrows the grant, this test is the place it shows.
        var identity = new DeviceIdentity(DeviceId, SiteId, "production", role, 1);

        Assert.True(TenantAuthorizationEvaluator.Evaluate(identity, SiteId, "production", DeviceCapability.ReadSiteData).Allowed);
        Assert.True(TenantAuthorizationEvaluator.Evaluate(identity, SiteId, "production", DeviceCapability.WriteSiteData).Allowed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Operator")]
    [InlineData(" operator")]
    public void IsKnown_matches_exactly(string? role)
    {
        // Ordinal and case-sensitive on purpose: the role is compared against what the token
        // service signed, not against user input, so a near-miss means the token is not one of
        // ours and tolerating the difference would only widen what counts as valid.
        Assert.False(DeviceRoles.IsKnown(role));
    }
}
