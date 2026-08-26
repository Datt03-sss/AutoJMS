using AutoJMS.DataHub.Api.Configuration;
using AutoJMS.DataHub.Api.Infrastructure;

namespace AutoJMS.DataHub.Api.Auth;

public static class DeviceAuthenticationContext
{
    public const string ItemKey = "DataHub.DeviceIdentity";

    /// <summary>
    /// HMAC(enrollment pepper, bearer token) for the current request, matched against
    /// the <c>devices.credential_hash</c> recorded at enrollment. Only the digest is
    /// carried forward — the bearer token itself never enters
    /// <see cref="HttpContext.Items"/>, so nothing downstream can leak it.
    /// </summary>
    public const string CredentialHashItemKey = "DataHub.DeviceCredentialHash";
}

public sealed class DeviceAuthenticationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        IDeviceTokenService tokenService,
        DataHubRuntimeOptions options)
    {
        if (IsAnonymousPath(context.Request.Path) || IsAlreadyAuthenticatedAdmin(context))
        {
            await next(context);
            return;
        }

        var authorization = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization)
            && context.Request.Path.StartsWithSegments("/hubs/site", StringComparison.OrdinalIgnoreCase)
            && context.Request.Query.TryGetValue("access_token", out var queryToken)
            && !string.IsNullOrWhiteSpace(queryToken.ToString()))
        {
            // SignalR's WebSocket transport cannot always set an Authorization
            // header. Accept its standard access_token query value only on the
            // authenticated hub path; never log or echo it.
            authorization = "Bearer " + queryToken.ToString();
        }
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            await ApiProblemWriter.WriteAsync(context, StatusCodes.Status401Unauthorized, ApiProblemCodes.Unauthorized, "A device bearer token is required.");
            return;
        }

        var token = authorization["Bearer ".Length..].Trim();
        var validation = await tokenService.ValidateAsync(token, context.RequestAborted);
        if (!validation.Succeeded || validation.Identity is null)
        {
            await ApiProblemWriter.WriteAsync(context, StatusCodes.Status401Unauthorized, ApiProblemCodes.Unauthorized, "The device bearer token is invalid or expired.");
            return;
        }

        if (!string.Equals(validation.Identity.Channel, options.Channel, StringComparison.Ordinal))
        {
            await ApiProblemWriter.WriteAsync(context, StatusCodes.Status403Forbidden, ApiProblemCodes.ChannelMismatch, "The device token belongs to another deployment channel.");
            return;
        }

        // Rejected here rather than per-endpoint because /hubs/site has no route handler
        // to ask: the hub only checks that an identity exists, so a token carrying a role
        // this build does not recognise would otherwise still join the site group and
        // receive every doorbell.
        if (!DeviceRoles.IsKnown(validation.Identity.Role))
        {
            await ApiProblemWriter.WriteAsync(context, StatusCodes.Status403Forbidden, ApiProblemCodes.Forbidden, "The device token carries a role this deployment does not recognise.");
            return;
        }

        context.Items[DeviceAuthenticationContext.ItemKey] = validation.Identity;
        // Derived here because this is the only place holding the raw token, and only
        // the digest travels onward. DeviceStatusMiddleware matches it against the
        // enrolled row, so a signature-valid token whose body was assembled by an
        // attacker rather than issued by enrollment fails at the database.
        context.Items[DeviceAuthenticationContext.CredentialHashItemKey] =
            DeviceCredentialHash.Compute(options.EnrollmentPepper, token);
        await next(context);
    }

    /// <summary>
    /// Enumerated, not prefix-matched: a future /health/config or /health/metrics
    /// must be an explicit decision to publish, not something that inherits
    /// anonymity from the segment it happens to sit under.
    /// </summary>
    private static readonly HashSet<string> AnonymousPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/health/live",
        "/health/ready"
    };

    private static bool IsAnonymousPath(PathString path)
        => AnonymousPaths.Contains(path.Value ?? "")
            || path.StartsWithSegments("/api/v1/devices/enroll", StringComparison.OrdinalIgnoreCase)
            || IsControlPlaneReadPath(path);

    /// <summary>
    /// The manifest/config control plane. The desktop fetches these with no
    /// credentials — it must read tier definitions and the update manifest before it
    /// has a device to enroll — so device auth would turn every policy fetch into a
    /// silent 401 and a safe-default downgrade. The published objects carry no
    /// secrets, and ManifestObjectPath bounds what can be served.
    /// </summary>
    private static bool IsControlPlaneReadPath(PathString path)
    {
        foreach (var container in Manifests.ManifestObjectPath.AllowedPrefixes)
        {
            if (path.StartsWithSegments("/" + container, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Lets an admin request that already presented the operator token through
    /// without also demanding a device token. Keyed on the marker rather than on the
    /// path, so removing <see cref="AdminAuthenticationMiddleware"/> from the
    /// pipeline closes the admin route instead of opening it.
    /// </summary>
    private static bool IsAlreadyAuthenticatedAdmin(HttpContext context)
        => context.IsAdminAuthenticated();
}

public static class DeviceAuthenticationExtensions
{
    public static DeviceIdentity? GetDeviceIdentity(this HttpContext context)
        => context.Items.TryGetValue(DeviceAuthenticationContext.ItemKey, out var value)
            ? value as DeviceIdentity
            : null;

    /// <summary>
    /// The credential digest for this request, or null when device authentication did
    /// not run. Null must never be treated as "no check required" — see
    /// <see cref="DeviceStatusMiddleware"/>.
    /// </summary>
    public static string? GetDeviceCredentialHash(this HttpContext context)
        => context.Items.TryGetValue(DeviceAuthenticationContext.CredentialHashItemKey, out var value)
            ? value as string
            : null;
}
