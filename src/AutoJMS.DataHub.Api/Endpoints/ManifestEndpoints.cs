using AutoJMS.DataHub.Api.Auth;
using AutoJMS.DataHub.Api.Infrastructure;
using AutoJMS.DataHub.Api.Manifests;

namespace AutoJMS.DataHub.Api.Endpoints;

/// <summary>
/// The control plane the desktop reads its policy from and the release script
/// publishes to.
///
/// Reads are anonymous by necessity, not by convenience: VpsManifestService fetches
/// with a bare HttpClient, and a station must be able to read tier definitions and
/// the update manifest before it has enrolled a device. That makes
/// <see cref="ManifestObjectPath"/> the entire perimeter, and it means published
/// objects must never contain a secret.
/// </summary>
public static class ManifestEndpoints
{
    public static IEndpointRouteBuilder MapManifestEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // GET and HEAD, not MapGet: MapGet registers GET alone, so a HEAD answered 405
        // even though the same object was being served over GET. Nothing in the desktop
        // sends HEAD — VpsManifestService only ever calls GetStringAsync — but an
        // operator checking "is the policy actually published?" should not have to
        // download it, and the publish script verifies its own work that way. Kestrel
        // suppresses the body for a HEAD response, so the handler needs no branch.
        foreach (var container in ManifestObjectPath.AllowedPrefixes)
            endpoints.MapMethods(
                $"/{container}/{{**objectPath}}",
                [HttpMethods.Get, HttpMethods.Head],
                (HttpContext context, ManifestStore store, string objectPath)
                    => ReadAsync(context, store, $"{container}/{objectPath}"));

        endpoints.MapPut("/api/v1/admin/manifests/{**objectPath}", PublishAsync)
            .RequireRateLimiting("manifestAdmin");

        return endpoints;
    }

    private static async Task<IResult> ReadAsync(HttpContext context, ManifestStore store, string requestedPath)
    {
        if (!ManifestObjectPath.TryCanonicalize(requestedPath, out var canonical, out _))
        {
            // Deliberately vague: an anonymous caller learns whether a path is
            // servable, never why a rejected one failed.
            return Problem(StatusCodes.Status404NotFound, ApiProblemCodes.NotFound, "The requested object does not exist.");
        }

        var stored = await store.ReadAsync(canonical, context.RequestAborted);
        if (stored is null)
            return Problem(StatusCodes.Status404NotFound, ApiProblemCodes.NotFound, "The requested object does not exist.");

        context.Response.Headers.ETag = stored.ETag;
        // Short and revalidated: a policy change must reach the fleet within minutes,
        // but a restart storm should not re-download every object.
        context.Response.Headers.CacheControl = "public, max-age=60, must-revalidate";

        if (RequestMatchesETag(context.Request.Headers.IfNoneMatch, stored.ETag))
            return Results.StatusCode(StatusCodes.Status304NotModified);

        context.Response.Headers.LastModified = stored.LastModified.ToString("R");
        return Results.Bytes(stored.Content, stored.ContentType);
    }

    private static async Task<IResult> PublishAsync(HttpContext context, ManifestStore store, string objectPath, ILoggerFactory loggerFactory)
    {
        // Re-checked here, not only in the middleware: if the pipeline is ever
        // reordered this endpoint still refuses to publish.
        if (!context.IsAdminAuthenticated())
            return Problem(StatusCodes.Status401Unauthorized, ApiProblemCodes.Unauthorized, "An administrative bearer token is required.");

        if (!ManifestObjectPath.TryCanonicalize(objectPath, out var canonical, out var reason))
            return Problem(StatusCodes.Status400BadRequest, ApiProblemCodes.BadRequest, reason);

        var content = await ReadBodyAsync(context, ManifestStore.MaximumObjectBytes, context.RequestAborted);
        if (content is null)
            return Problem(StatusCodes.Status413PayloadTooLarge, ApiProblemCodes.BadRequest, $"The object exceeds {ManifestStore.MaximumObjectBytes} bytes.");

        if (!ManifestStore.TryValidatePayload(canonical, content, out var payloadReason))
            return Problem(StatusCodes.Status400BadRequest, ApiProblemCodes.BadRequest, payloadReason);

        var result = await store.WriteAsync(canonical, content, context.RequestAborted);

        // Audited through the log, not audit_logs: that table needs a Postgres
        // transaction, and a publish must still succeed when the database is down.
        loggerFactory.CreateLogger("DataHub.Manifests").LogInformation(
            "Published {ObjectPath} ({Length} bytes, etag {ETag}) from {RemoteAddress}.",
            result.CanonicalPath,
            result.Length,
            result.ETag,
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown");

        context.Response.Headers.ETag = result.ETag;
        return result.Created
            ? Results.Created($"/{result.CanonicalPath}", new { objectPath = result.CanonicalPath, etag = result.ETag, length = result.Length })
            : Results.Ok(new { objectPath = result.CanonicalPath, etag = result.ETag, length = result.Length });
    }

    /// <summary>
    /// Buffers the body with a hard ceiling. Kestrel already caps the request size,
    /// but a chunked request with no Content-Length must not be able to grow the
    /// buffer past the same limit.
    /// </summary>
    private static async Task<byte[]?> ReadBodyAsync(HttpContext context, int maximumBytes, CancellationToken cancellationToken)
    {
        if (context.Request.ContentLength > maximumBytes)
            return null;

        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var read = await context.Request.Body.ReadAsync(chunk, cancellationToken);
            if (read == 0)
                break;

            if (buffer.Length + read > maximumBytes)
                return null;

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static bool RequestMatchesETag(Microsoft.Extensions.Primitives.StringValues ifNoneMatch, string etag)
    {
        foreach (var header in ifNoneMatch)
        {
            if (string.IsNullOrWhiteSpace(header))
                continue;

            foreach (var candidate in header.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (candidate == "*")
                    return true;

                // A cache may weaken the tag on the way back; the content hash is the
                // same either way.
                var normalized = candidate.StartsWith("W/", StringComparison.Ordinal) ? candidate[2..] : candidate;
                if (string.Equals(normalized, etag, StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
    }

    private static IResult Problem(int status, string code, string detail)
        => ApiProblemWriter.Result(status, code, detail);
}
