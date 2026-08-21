using AutoJMS.DataHub.Api.Configuration;
using AutoJMS.DataHub.Api.Infrastructure;

namespace AutoJMS.DataHub.Api.Auth;

public static class DeviceAuthenticationContext
{
    public const string ItemKey = "DataHub.DeviceIdentity";
}

public sealed class DeviceAuthenticationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        IDeviceTokenService tokenService,
        DataHubRuntimeOptions options)
    {
        if (IsAnonymousPath(context.Request.Path))
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

        context.Items[DeviceAuthenticationContext.ItemKey] = validation.Identity;
        await next(context);
    }

    private static bool IsAnonymousPath(PathString path)
        => path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/api/v1/devices/enroll", StringComparison.OrdinalIgnoreCase);
}

public static class DeviceAuthenticationExtensions
{
    public static DeviceIdentity? GetDeviceIdentity(this HttpContext context)
        => context.Items.TryGetValue(DeviceAuthenticationContext.ItemKey, out var value)
            ? value as DeviceIdentity
            : null;
}
