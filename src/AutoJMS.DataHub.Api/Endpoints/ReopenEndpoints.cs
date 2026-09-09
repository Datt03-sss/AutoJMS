using AutoJMS.DataHub.Api.Auth;
using AutoJMS.DataHub.Api.Infrastructure;
using AutoJMS.DataHub.Api.Services;

namespace AutoJMS.DataHub.Api.Endpoints;

public static class ReopenEndpoints
{
    public static IEndpointRouteBuilder MapReopenEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Under /api/v1/admin because that prefix is the only thing
        // AdminAuthenticationMiddleware inspects: outside it the middleware returns early
        // and never sets the marker IsAdminAuthenticated() reads, so the contract's
        // /api/v1/sites/... placement would have answered 401 to every caller. Same rate
        // limiter as the other operator routes.
        endpoints.MapPost(
                "/api/v1/admin/sites/{siteId:guid}/waybills/{waybillNo}/reopen",
                (HttpContext context, Guid siteId, string waybillNo, ReopenRepository repository, IDoorbellPublisher publisher, ILoggerFactory loggerFactory)
                    => HandleAsync(context, siteId, waybillNo, repository, publisher, loggerFactory.CreateLogger("DataHub.Reopen")))
            .RequireRateLimiting("manifestAdmin");
        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        Guid siteId,
        string waybillNo,
        ReopenRepository repository,
        IDoorbellPublisher publisher,
        ILogger logger)
    {
        // Re-checked in the handler rather than trusted from the pipeline, matching
        // ManifestEndpoints: if the middleware order is ever changed, this stays closed
        // instead of silently opening an operator route to anyone.
        if (!context.IsAdminAuthenticated())
            return Problem(StatusCodes.Status401Unauthorized, ApiProblemCodes.Unauthorized, "An administrative bearer token is required.");

        var idempotencyKey = context.Request.Headers["Idempotency-Key"].ToString().Trim();
        if (idempotencyKey.Length is < 8 or > 128)
            return Problem(StatusCodes.Status400BadRequest, ApiProblemCodes.BadRequest, "Idempotency-Key must contain between 8 and 128 characters.");

        // The actor is the authenticated principal, never anything the caller supplied. An
        // audit trail an operator can write their own name into records nothing.
        var result = await repository.ReopenAsync(siteId, waybillNo, idempotencyKey, "admin-token", context.RequestAborted);
        if (!result.Succeeded)
            return Problem(result.StatusCode, result.ProblemCode ?? ApiProblemCodes.BadRequest, result.Detail ?? "Reopen failed.");

        if (result.Doorbells.Count > 0)
        {
            try
            {
                await publisher.PublishAsync(result.Doorbells, context.RequestAborted);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                // The committed delta remains recoverable through the cursor.
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "SignalR doorbell publication failed after committed reopen for site {SiteId}.", siteId);
            }
        }
        return Results.Ok(result.Response);
    }

    private static IResult Problem(int status, string code, string detail)
        => ApiProblemWriter.Result(status, code, detail);
}
