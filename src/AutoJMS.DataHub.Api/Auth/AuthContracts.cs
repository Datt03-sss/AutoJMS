namespace AutoJMS.DataHub.Api.Auth;

public static class ApiProblemCodes
{
    public const string BadRequest = "BAD_REQUEST";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string Forbidden = "FORBIDDEN";
    public const string ChannelMismatch = "CHANNEL_MISMATCH";
    public const string SiteNotLicensed = "SITE_NOT_LICENSED";
    public const string NotFound = "NOT_FOUND";
    public const string LeaderFenced = "LEADER_FENCED";
    public const string LeaseHeld = "LEASE_HELD";
    public const string ServiceUnavailable = "SERVICE_UNAVAILABLE";
}

public sealed record DeviceIdentity(
    Guid DeviceId,
    Guid SiteId,
    string Channel,
    string Role,
    int TokenVersion);

/// <summary>
/// Every role a device token may carry. Enrollment issues only <see cref="Operator"/>
/// today; the other two exist because the token format already carries the claim and a
/// role this set does not name must be rejected rather than quietly treated as valid.
/// </summary>
public static class DeviceRoles
{
    public const string Operator = "operator";
    public const string Leader = "leader";
    public const string Admin = "admin";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.Ordinal) { Operator, Leader, Admin };

    public static bool IsKnown(string? role) => role is not null && All.Contains(role);
}

/// <summary>What an endpoint needs the caller's role to be allowed to do.</summary>
public enum DeviceCapability
{
    /// <summary>Reading a site's changes or projection snapshot.</summary>
    ReadSiteData,

    /// <summary>Mutating site state: ingest, observations, and the bulk-fetch lease.</summary>
    WriteSiteData
}

/// <summary>
/// Maps a token's role to what it may do.
///
/// All three named roles currently hold both capabilities, and saying so plainly is
/// better than implying a hierarchy the endpoints do not have: every station enrolls as
/// <c>operator</c> and must keep reading its site and writing its own scans, and no
/// endpoint is gated on <c>admin</c> — the manifest control plane authenticates with the
/// operator token, not with a device token.
///
/// What this closes is the open door underneath. The role claim was carried through
/// enrollment, signed, and then never consulted, so a token whose role read anything at
/// all — a future <c>readonly</c> or <c>quarantined</c> value, or a hand-assembled
/// string — was as good as an operator's. An unlisted role now grants nothing, which
/// makes adding a restricted role a change to this table rather than a change to every
/// endpoint that forgot to ask.
/// </summary>
public static class DeviceRolePolicy
{
    private static readonly IReadOnlySet<DeviceCapability> FullSiteAccess =
        new HashSet<DeviceCapability> { DeviceCapability.ReadSiteData, DeviceCapability.WriteSiteData };

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<DeviceCapability>> Granted =
        new Dictionary<string, IReadOnlySet<DeviceCapability>>(StringComparer.Ordinal)
        {
            [DeviceRoles.Operator] = FullSiteAccess,
            [DeviceRoles.Leader] = FullSiteAccess,
            [DeviceRoles.Admin] = FullSiteAccess
        };

    public static bool Allows(string? role, DeviceCapability capability)
        => role is not null
            && Granted.TryGetValue(role, out var capabilities)
            && capabilities.Contains(capability);
}

public sealed record DeviceTokenDescriptor(
    Guid DeviceId,
    Guid SiteId,
    string Channel,
    string Role,
    int TokenVersion,
    DateTimeOffset ExpiresAt);

public sealed record DeviceTokenValidationResult(
    bool Succeeded,
    DeviceIdentity? Identity = null,
    string? FailureCode = null)
{
    public static DeviceTokenValidationResult Failure(string code) => new(false, null, code);
    public static DeviceTokenValidationResult Success(DeviceIdentity identity) => new(true, identity);
}

public interface IDeviceTokenService
{
    string Issue(DeviceTokenDescriptor descriptor);
    ValueTask<DeviceTokenValidationResult> ValidateAsync(string token, CancellationToken cancellationToken);
}

public sealed record LicenseAssertionIdentity(
    string Channel,
    IReadOnlySet<string> SiteCodes,
    DateTimeOffset ExpiresAt,
    string? DataHubUrl,
    int TokenVersion,
    int Seats);

public sealed record LicenseAssertionValidationResult(
    bool Succeeded,
    LicenseAssertionIdentity? Identity = null,
    string? FailureCode = null)
{
    public static LicenseAssertionValidationResult Failure(string code) => new(false, null, code);
    public static LicenseAssertionValidationResult Success(LicenseAssertionIdentity identity) => new(true, identity);
}

public interface ILicenseAssertionValidator
{
    ValueTask<LicenseAssertionValidationResult> ValidateAsync(string assertion, CancellationToken cancellationToken);
}

public sealed record StagingLicenseAssertionDescriptor(
    IReadOnlyCollection<string> SiteCodes,
    DateTimeOffset ExpiresAt,
    string? DataHubUrl,
    int Seats,
    int TokenVersion);

public interface IStagingTestLicenseAssertionIssuer
{
    string Issue(StagingLicenseAssertionDescriptor descriptor);
}

public sealed record TenantAuthorizationResult(bool Allowed, string? ProblemCode)
{
    public static TenantAuthorizationResult Success() => new(true, null);
    public static TenantAuthorizationResult Failure(string code) => new(false, code);
}

public static class TenantAuthorizationEvaluator
{
    /// <param name="requiredCapability">
    /// What the endpoint is about to do. Required rather than defaulted: a new endpoint
    /// that forgets to say gets a compile error instead of silently inheriting read.
    /// </param>
    public static TenantAuthorizationResult Evaluate(
        DeviceIdentity identity,
        Guid routeSiteId,
        string deploymentChannel,
        DeviceCapability requiredCapability)
    {
        if (!string.Equals(identity.Channel, deploymentChannel, StringComparison.Ordinal))
            return TenantAuthorizationResult.Failure(ApiProblemCodes.ChannelMismatch);

        if (identity.SiteId != routeSiteId)
            return TenantAuthorizationResult.Failure(ApiProblemCodes.SiteNotLicensed);

        // Ordered last so a cross-tenant attempt still reports SITE_NOT_LICENSED: the
        // role is the least interesting reason to refuse a device that was asking about
        // somebody else's site to begin with.
        if (!DeviceRolePolicy.Allows(identity.Role, requiredCapability))
            return TenantAuthorizationResult.Failure(ApiProblemCodes.Forbidden);

        return TenantAuthorizationResult.Success();
    }
}
