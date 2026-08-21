namespace AutoJMS.DataHub.Api.Auth;

public static class ApiProblemCodes
{
    public const string BadRequest = "BAD_REQUEST";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string Forbidden = "FORBIDDEN";
    public const string ChannelMismatch = "CHANNEL_MISMATCH";
    public const string SiteNotLicensed = "SITE_NOT_LICENSED";
    public const string NotFound = "NOT_FOUND";
    public const string ServiceUnavailable = "SERVICE_UNAVAILABLE";
}

public sealed record DeviceIdentity(
    Guid DeviceId,
    Guid SiteId,
    string Channel,
    string Role,
    int TokenVersion);

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
    public static TenantAuthorizationResult Evaluate(
        DeviceIdentity identity,
        Guid routeSiteId,
        string deploymentChannel)
    {
        if (!string.Equals(identity.Channel, deploymentChannel, StringComparison.Ordinal))
            return TenantAuthorizationResult.Failure(ApiProblemCodes.ChannelMismatch);

        if (identity.SiteId != routeSiteId)
            return TenantAuthorizationResult.Failure(ApiProblemCodes.SiteNotLicensed);

        return TenantAuthorizationResult.Success();
    }
}
