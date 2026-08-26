using AutoJMS.DataHub.Api.Auth;
using AutoJMS.DataHub.Api.Configuration;
using AutoJMS.DataHub.Api.Infrastructure;

namespace AutoJMS.DataHub.Api.Endpoints;

public static class SyncEndpoints
{
    /// <summary>Matches the ChangeLimit schema published in datahub-v1.yaml.</summary>
    private const int MaximumChangeLimit = 500;

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
        // The repository clamps, but clamping alone is a silent lie: a client asking for
        // 2000 got 500 back and no way to tell that its own page size was ignored. The
        // published contract says 1-500, so anything else is a client bug worth naming.
        if (limit is < 1 or > MaximumChangeLimit)
            return Problem(StatusCodes.Status400BadRequest, ApiProblemCodes.BadRequest, $"limit must be between 1 and {MaximumChangeLimit}.");

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
        DataHubRuntimeOptions options,
        ILoggerFactory loggerFactory,
        // The client has always sent ?limit=, but this parameter did not exist, so
        // model binding discarded it and every snapshot returned the site's entire
        // projection table in one response body.
        int limit = ChangeRepository.DefaultSnapshotRows)
    {
        var authorization = Authorize(context.GetDeviceIdentity(), siteId, options.Channel);
        if (!authorization.Allowed)
            return Problem(StatusCodes.Status403Forbidden, authorization.ProblemCode!, "The device is not authorized for this site or deployment.");
        if (limit is < 1 or > ChangeRepository.MaximumSnapshotRows)
            return Problem(StatusCodes.Status400BadRequest, ApiProblemCodes.BadRequest, $"limit must be between 1 and {ChangeRepository.MaximumSnapshotRows}.");

        try
        {
            var snapshot = await repository.ReadSnapshotAsync(siteId, limit, context.RequestAborted);
            if (snapshot.Truncated)
            {
                // Operator-visible, because the caller cannot fix this on its own: the
                // site has outgrown a single-response snapshot and needs the cursor feed.
                loggerFactory.CreateLogger("DataHub.Sync").LogWarning(
                    "Snapshot for site {SiteId} was truncated at {Limit} rows; the client is missing the remainder until those waybills change.",
                    siteId,
                    limit);
            }
            return Results.Ok(snapshot);
        }
        catch (KeyNotFoundException)
        {
            return Problem(StatusCodes.Status404NotFound, ApiProblemCodes.NotFound, "The site has not been provisioned.");
        }
    }

    private static TenantAuthorizationResult Authorize(DeviceIdentity? identity, Guid siteId, string channel)
        => identity is null
            ? TenantAuthorizationResult.Failure(ApiProblemCodes.Unauthorized)
            : TenantAuthorizationEvaluator.Evaluate(identity, siteId, channel, DeviceCapability.ReadSiteData);

    private static IResult Problem(int status, string code, string detail)
        => ApiProblemWriter.Result(status, code, detail);
}
