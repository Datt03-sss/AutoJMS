using AutoJMS.DataHub.Api.Infrastructure;

namespace AutoJMS.DataHub.Api.Auth;

public sealed class DeviceStatusMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        DeviceRepository deviceRepository,
        IngressIpRateLimiter rateLimiter)
    {
        var identity = context.GetDeviceIdentity();
        if (identity is null)
        {
            await next(context);
            return;
        }

        using var lease = await rateLimiter.AcquireDeviceAsync(identity.DeviceId, context.RequestAborted);
        if (!lease.IsAcquired)
        {
            context.Response.Headers.RetryAfter = "60";
            await ApiProblemWriter.WriteAsync(
                context,
                StatusCodes.Status429TooManyRequests,
                "RATE_LIMITED",
                "Too many requests for this device; retry after the indicated delay.");
            return;
        }

        if (!await deviceRepository.TouchActiveAsync(identity, context.RequestAborted))
        {
            await ApiProblemWriter.WriteAsync(
                context,
                StatusCodes.Status401Unauthorized,
                ApiProblemCodes.Unauthorized,
                "The device is revoked, disabled, or no longer enrolled.");
            return;
        }

        await next(context);
    }
}
