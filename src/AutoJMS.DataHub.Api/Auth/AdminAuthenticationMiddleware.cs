using System.Security.Cryptography;
using System.Text;
using AutoJMS.DataHub.Api.Configuration;
using AutoJMS.DataHub.Api.Infrastructure;

namespace AutoJMS.DataHub.Api.Auth;

public static class AdminAuthenticationContext
{
    public const string ItemKey = "DataHub.AdminAuthenticated";

    /// <summary>Every route guarded by the operator token lives under this prefix.</summary>
    public const string PathPrefix = "/api/v1/admin";
}

/// <summary>
/// Authenticates the operator token used by release/build-release.ps1 to publish
/// control-plane objects. Runs before <see cref="DeviceAuthenticationMiddleware"/>
/// because a device token must never be sufficient for an admin route: an enrolled
/// station is a customer machine, not the publisher.
///
/// Fail-closed in both directions. With no token configured the route answers 503
/// rather than accepting anything, and the marker it sets on success is what the
/// device middleware and the endpoint itself both require — so a future reordering
/// of the pipeline cannot silently leave the admin route open.
/// </summary>
public sealed class AdminAuthenticationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        DataHubRuntimeOptions options,
        IngressIpRateLimiter limiter,
        ILogger<AdminAuthenticationMiddleware> logger)
    {
        if (!context.Request.Path.StartsWithSegments(AdminAuthenticationContext.PathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        // A publish is a handful of requests per release, so a tight per-IP budget
        // costs the operator nothing and removes online guessing as an option.
        using var lease = await limiter.AcquireAdminAsync(context);
        if (!lease.IsAcquired)
        {
            context.Response.Headers.RetryAfter = "60";
            await ApiProblemWriter.WriteAsync(
                context,
                StatusCodes.Status429TooManyRequests,
                "RATE_LIMITED",
                "Too many administrative requests from this network address; retry after the indicated delay.");
            return;
        }

        if (string.IsNullOrWhiteSpace(options.ManifestAdminToken))
        {
            logger.LogError(
                "Rejected {Method} {Path}: DATAHUB_ADMIN_TOKEN is not configured on this host.",
                context.Request.Method,
                context.Request.Path);
            await ApiProblemWriter.WriteAsync(
                context,
                StatusCodes.Status503ServiceUnavailable,
                ApiProblemCodes.ServiceUnavailable,
                "Administrative publishing is not configured on this host.");
            return;
        }

        var authorization = context.Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            || !MatchesConfiguredToken(authorization["Bearer ".Length..].Trim(), options.ManifestAdminToken))
        {
            logger.LogWarning(
                "Rejected {Method} {Path}: the administrative bearer token is missing or incorrect.",
                context.Request.Method,
                context.Request.Path);
            await ApiProblemWriter.WriteAsync(
                context,
                StatusCodes.Status401Unauthorized,
                ApiProblemCodes.Unauthorized,
                "An administrative bearer token is required.");
            return;
        }

        context.Items[AdminAuthenticationContext.ItemKey] = true;
        await next(context);
    }

    /// <summary>
    /// Constant-time comparison. Lengths are compared first because
    /// <see cref="CryptographicOperations.FixedTimeEquals"/> requires equal spans;
    /// that leaks only the configured token's length, not its content.
    /// </summary>
    private static bool MatchesConfiguredToken(string presented, string configured)
    {
        var presentedBytes = Encoding.UTF8.GetBytes(presented);
        var configuredBytes = Encoding.UTF8.GetBytes(configured);
        return presentedBytes.Length == configuredBytes.Length
            && CryptographicOperations.FixedTimeEquals(presentedBytes, configuredBytes);
    }
}

public static class AdminAuthenticationExtensions
{
    /// <summary>
    /// True only when <see cref="AdminAuthenticationMiddleware"/> validated the
    /// operator token for this request. Admin endpoints re-check this so they stay
    /// closed even if the middleware is removed from the pipeline.
    /// </summary>
    public static bool IsAdminAuthenticated(this HttpContext context)
        => context.Items.TryGetValue(AdminAuthenticationContext.ItemKey, out var value) && value is true;
}
