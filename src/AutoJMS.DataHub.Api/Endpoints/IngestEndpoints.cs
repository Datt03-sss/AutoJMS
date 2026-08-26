using System.Globalization;
using System.Text.Json;
using AutoJMS.DataHub.Api.Auth;
using AutoJMS.DataHub.Api.Configuration;
using AutoJMS.DataHub.Api.Infrastructure;
using AutoJMS.DataHub.Api.Services;

namespace AutoJMS.DataHub.Api.Endpoints;

public static class IngestEndpoints
{
    private const long MaximumBodyBytes = 1024 * 1024;

    public static IEndpointRouteBuilder MapIngestEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/sites/{siteId:guid}/jms/ingest", (HttpContext context, Guid siteId, IngestRequest request, IngestPipeline pipeline, IDoorbellPublisher publisher, DataHubRuntimeOptions options, ILoggerFactory loggerFactory)
            => HandleAsync(context, siteId, request, pipeline, publisher, options, loggerFactory.CreateLogger("DataHub.Ingest"), requireFence: true))
            .RequireRateLimiting("device");
        endpoints.MapPost("/api/v1/sites/{siteId:guid}/jms/observations", (HttpContext context, Guid siteId, IngestRequest request, IngestPipeline pipeline, IDoorbellPublisher publisher, DataHubRuntimeOptions options, ILoggerFactory loggerFactory)
            => HandleAsync(context, siteId, request, pipeline, publisher, options, loggerFactory.CreateLogger("DataHub.Ingest"), requireFence: false))
            .RequireRateLimiting("device");
        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        Guid siteId,
        IngestRequest request,
        IngestPipeline pipeline,
        IDoorbellPublisher publisher,
        DataHubRuntimeOptions options,
        ILogger logger,
        bool requireFence)
    {
        if (context.Request.ContentLength is > MaximumBodyBytes)
            return Problem(StatusCodes.Status413PayloadTooLarge, "PAYLOAD_TOO_LARGE", "The request body exceeds the 1 MiB limit.");

        var identity = context.GetDeviceIdentity();
        if (identity is null)
            return Problem(StatusCodes.Status401Unauthorized, ApiProblemCodes.Unauthorized, "A device bearer token is required.");
        var authorization = TenantAuthorizationEvaluator.Evaluate(identity, siteId, options.Channel, DeviceCapability.WriteSiteData);
        if (!authorization.Allowed)
            return Problem(
                StatusCodes.Status403Forbidden,
                authorization.ProblemCode!,
                authorization.ProblemCode == ApiProblemCodes.Forbidden
                    ? "The device role may not write observations for this site."
                    : "The device is not authorized for this site or deployment.");

        var idempotencyKey = context.Request.Headers["Idempotency-Key"].ToString().Trim();
        if (idempotencyKey.Length is < 8 or > 128)
            return Problem(StatusCodes.Status400BadRequest, ApiProblemCodes.BadRequest, "Idempotency-Key must contain between 8 and 128 characters.");

        long? leaderTerm = null;
        if (requireFence)
        {
            var rawTerm = context.Request.Headers["X-Leader-Term"].ToString();
            if (!long.TryParse(rawTerm, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedTerm) || parsedTerm < 1)
                return Problem(StatusCodes.Status409Conflict, ApiProblemCodes.LeaderFenced, "X-Leader-Term is required for bulk ingest.");
            leaderTerm = parsedTerm;
        }

        if (request is null || request.Items is null)
            return Problem(StatusCodes.Status422UnprocessableEntity, "VALIDATION_FAILED", "The request must contain an items array.");
        if (request.Items.Any(item => item is null))
            return Problem(StatusCodes.Status422UnprocessableEntity, "VALIDATION_FAILED", "The items array cannot contain null observations.");

        var normalized = new IngestRequest
        {
            Items = request.Items.Select(item => item with { SiteId = siteId }).ToList()
        };
        if (normalized.Items is null || normalized.Items.Any(item =>
                string.IsNullOrWhiteSpace(item.WaybillNo)
                || item.Payload is null
                || item.Payload.Value.ValueKind != JsonValueKind.Object))
            return Problem(StatusCodes.Status422UnprocessableEntity, "VALIDATION_FAILED", "Every observation requires a waybillNo and object payload.");
        var operation = await pipeline.ExecuteAsync(siteId, identity.DeviceId, leaderTerm, requireFence, idempotencyKey, normalized, context.RequestAborted);
        if (!operation.Succeeded)
            return Problem(operation.StatusCode, operation.ProblemCode ?? ApiProblemCodes.BadRequest, operation.Detail ?? "Ingest failed.");

        if (operation.Doorbells.Count > 0)
        {
            try
            {
                await publisher.PublishAsync(operation.Doorbells, context.RequestAborted);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                // The committed delta remains recoverable through the cursor.
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "SignalR doorbell publication failed after committed ingest for site {SiteId}.", siteId);
            }
        }
        return Results.Ok(operation.Response);
    }

    private static IResult Problem(int status, string code, string detail)
        => ApiProblemWriter.Result(status, code, detail);
}
