using AutoJMS.DataHub.Api.Infrastructure;

namespace AutoJMS.DataHub.Api.Auth;

public static class DeviceAuthenticationContext
{
    public const string ItemKey = "DataHub.DeviceIdentity";
}

public sealed class DeviceAuthenticationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IDeviceTokenService tokenService)
    {
        if (IsAnonymousPath(context.Request.Path))
        {
            await next(context);
            return;
        }

        var authorization = context.Request.Headers.Authorization.ToString();
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
