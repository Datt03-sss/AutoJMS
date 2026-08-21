using AutoJMS.DataHub.Api.Auth;
using AutoJMS.DataHub.Api.Configuration;
using AutoJMS.DataHub.Api.Infrastructure;

namespace AutoJMS.DataHub.Api.Endpoints;

public static class SyncEndpoints
{
    public static IEndpointRouteBuilder MapSyncEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/sites/{siteId:guid}/changes", ReadChangesAsync).RequireRateLimiting("device");
        endpoints.MapGet("/api/v1/sites/{siteId:guid}/projections/snapshot", ReadSnapshotAsync).RequireRateLimiting("device");
        return endpoints;
    }

    private static async Task<IResult> ReadChangesAsync(
        HttpContext context,
        Guid siteId,
        ChangeRepository repository,
        DataHubRuntimeOptions options,
        long after = 0,
        int limit = 500)
    {
        var authorization = Authorize(context.GetDeviceIdentity(), siteId, options.Channel);
        if (!authorization.Allowed)
            return Problem(StatusCodes.Status403Forbidden, authorization.ProblemCode!, "The device is not authorized for this site or deployment.");
        if (after < 0)
            return Problem(StatusCodes.Status400BadRequest, ApiProblemCodes.BadRequest, "after cannot be negative.");

        (bool ResyncRequired, ChangePage? Page) result;
        try
        {
            result = await repository.ReadChangesAsync(siteId, after, limit, context.RequestAborted);
        }
        catch (KeyNotFoundException)
        {
            return Problem(StatusCodes.Status404NotFound, ApiProblemCodes.NotFound, "The site has not been provisioned.");
        }
        if (result.ResyncRequired)
            return Problem(StatusCodes.Status409Conflict, "RESYNC_REQUIRED", "The cursor is older than the retained change range; take a snapshot.");
        return Results.Ok(result.Page);
    }

    private static async Task<IResult> ReadSnapshotAsync(
        HttpContext context,
        Guid siteId,
        ChangeRepository repository,
        DataHubRuntimeOptions options)
    {
        var authorization = Authorize(context.GetDeviceIdentity(), siteId, options.Channel);
        if (!authorization.Allowed)
            return Problem(StatusCodes.Status403Forbidden, authorization.ProblemCode!, "The device is not authorized for this site or deployment.");

        try
        {
            return Results.Ok(await repository.ReadSnapshotAsync(siteId, context.RequestAborted));
        }
        catch (KeyNotFoundException)
        {
            return Problem(StatusCodes.Status404NotFound, ApiProblemCodes.NotFound, "The site has not been provisioned.");
        }
    }

    private static TenantAuthorizationResult Authorize(DeviceIdentity? identity, Guid siteId, string channel)
        => identity is null
            ? TenantAuthorizationResult.Failure(ApiProblemCodes.Unauthorized)
            : TenantAuthorizationEvaluator.Evaluate(identity, siteId, channel);

    private static IResult Problem(int status, string code, string detail)
        => ApiProblemWriter.Result(status, code, detail);
}
