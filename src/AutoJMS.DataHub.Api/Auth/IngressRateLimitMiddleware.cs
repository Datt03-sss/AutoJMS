using System.Threading.RateLimiting;
using AutoJMS.DataHub.Api.Infrastructure;

namespace AutoJMS.DataHub.Api.Auth;

public sealed class IngressIpRateLimiter : IAsyncDisposable
{
    private readonly PartitionedRateLimiter<string> _limiter = PartitionedRateLimiter.Create<string, string>(key =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 600,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    private readonly PartitionedRateLimiter<Guid> _deviceLimiter = PartitionedRateLimiter.Create<Guid, Guid>(deviceId =>
        RateLimitPartition.GetFixedWindowLimiter(deviceId, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 240,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));

    // A release publishes ~12 objects; nothing legitimate needs more than this, and a
    // tight budget is what keeps the operator token from being guessable online.
    private readonly PartitionedRateLimiter<string> _adminLimiter = PartitionedRateLimiter.Create<string, string>(key =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));

    public ValueTask<RateLimitLease> AcquireAsync(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return _limiter.AcquireAsync($"ingress:{address}", 1, context.RequestAborted);
    }

    public ValueTask<RateLimitLease> AcquireAdminAsync(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return _adminLimiter.AcquireAsync($"admin:{address}", 1, context.RequestAborted);
    }

    public ValueTask<RateLimitLease> AcquireDeviceAsync(Guid deviceId, CancellationToken cancellationToken)
        => _deviceLimiter.AcquireAsync(deviceId, 1, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await _limiter.DisposeAsync();
        await _deviceLimiter.DisposeAsync();
        await _adminLimiter.DisposeAsync();
    }
}

public sealed class IngressRateLimitMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IngressIpRateLimiter limiter)
    {
        using var lease = await limiter.AcquireAsync(context);
        if (!lease.IsAcquired)
        {
            context.Response.Headers.RetryAfter = "60";
            await ApiProblemWriter.WriteAsync(
                context,
                StatusCodes.Status429TooManyRequests,
                "RATE_LIMITED",
                "Too many requests from this network address; retry after the indicated delay.");
            return;
        }

        await next(context);
    }
}
