using AutoJMS.DataHub.Api.Auth;
using AutoJMS.DataHub.Api.Configuration;
using AutoJMS.DataHub.Api.Infrastructure;

namespace AutoJMS.DataHub.Api.Endpoints;

public sealed record LeaseTermRequest(long LeaderTerm);

public static class LeaseEndpoints
{
    public static IEndpointRouteBuilder MapLeaseEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/sites/{siteId:guid}/lease/acquire", AcquireAsync).RequireRateLimiting("device");
        endpoints.MapPost("/api/v1/sites/{siteId:guid}/lease/renew", RenewAsync).RequireRateLimiting("device");
        endpoints.MapPost("/api/v1/sites/{siteId:guid}/lease/release", ReleaseAsync).RequireRateLimiting("device");
        return endpoints;
    }

    private static async Task<IResult> AcquireAsync(
        HttpContext context,
        Guid siteId,
        LeaseRepository repository,
        DataHubRuntimeOptions options)
    {
        var identity = context.GetDeviceIdentity();
        var authorization = Authorize(identity, siteId, options.Channel);
        if (!authorization.Allowed)
            return Problem(authorization.ProblemCode!, DenialDetail(authorization.ProblemCode!));
        var result = await repository.AcquireAsync(siteId, identity!.DeviceId, context.RequestAborted);
        return ToResult(result);
    }

    private static async Task<IResult> RenewAsync(
        HttpContext context,
        Guid siteId,
        LeaseTermRequest request,
        LeaseRepository repository,
        DataHubRuntimeOptions options)
    {
        if (request is null)
            return Problem(ApiProblemCodes.BadRequest, "A lease term is required.", StatusCodes.Status422UnprocessableEntity);
        var identity = context.GetDeviceIdentity();
        var authorization = Authorize(identity, siteId, options.Channel);
        if (!authorization.Allowed)
            return Problem(authorization.ProblemCode!, "The device is not authorized for this site.");
        if (request.LeaderTerm < 1)
            return Problem(ApiProblemCodes.BadRequest, "leaderTerm must be positive.");
        return ToResult(await repository.RenewAsync(siteId, identity!.DeviceId, request.LeaderTerm, context.RequestAborted));
    }

    private static async Task<IResult> ReleaseAsync(
        HttpContext context,
        Guid siteId,
        LeaseTermRequest request,
        LeaseRepository repository,
        DataHubRuntimeOptions options)
    {
        if (request is null)
            return Problem(ApiProblemCodes.BadRequest, "A lease term is required.", StatusCodes.Status422UnprocessableEntity);
        var identity = context.GetDeviceIdentity();
        var authorization = Authorize(identity, siteId, options.Channel);
        if (!authorization.Allowed)
            return Problem(authorization.ProblemCode!, "The device is not authorized for this site.");
        if (request.LeaderTerm < 1)
            return Problem(ApiProblemCodes.BadRequest, "leaderTerm must be positive.");
        return ToResult(await repository.ReleaseAsync(siteId, identity!.DeviceId, request.LeaderTerm, context.RequestAborted));
    }

    private static string DenialDetail(string problemCode) => problemCode switch
    {
        ApiProblemCodes.ChannelMismatch => "The device channel does not match this deployment.",
        ApiProblemCodes.Forbidden => "The device role may not take the site bulk-fetch lease.",
        _ => "The requested site is not assigned to this device."
    };

    // The lease decides who may bulk-fetch, so acquiring or releasing it is a write even
    // though the body carries no rows.
    private static TenantAuthorizationResult Authorize(DeviceIdentity? identity, Guid siteId, string channel)
        => identity is null
            ? TenantAuthorizationResult.Failure(ApiProblemCodes.Unauthorized)
            : TenantAuthorizationEvaluator.Evaluate(identity, siteId, channel, DeviceCapability.WriteSiteData);

    private static IResult ToResult(LeaseOperationResult operation)
    {
        if (operation.Succeeded) return Results.Ok(operation.State);
        var status = operation.ProblemCode switch
        {
            ApiProblemCodes.NotFound => StatusCodes.Status404NotFound,
            ApiProblemCodes.LeaseHeld or ApiProblemCodes.LeaderFenced => StatusCodes.Status409Conflict,
            ApiProblemCodes.ChannelMismatch or ApiProblemCodes.SiteNotLicensed => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status400BadRequest
        };
        return Problem(operation.ProblemCode ?? ApiProblemCodes.BadRequest, operation.Detail ?? "Lease operation failed.", status);
    }

    private static IResult Problem(string code, string detail, int status = StatusCodes.Status403Forbidden)
        => ApiProblemWriter.Result(status, code, detail);
}
