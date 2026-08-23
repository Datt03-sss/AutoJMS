using AutoJMS.DataHub.Api.Auth;
using AutoJMS.DataHub.Api.Configuration;
using AutoJMS.DataHub.Api.Infrastructure;

namespace AutoJMS.DataHub.Api.Endpoints;

public sealed record EnrollRequest(string SiteCode, string DeviceName, string? Role);

public sealed record EnrollmentResponse(
    Guid DeviceId,
    Guid SiteId,
    string SiteCode,
    string Channel,
    string TokenType,
    string DeviceToken,
    int TokenVersion,
    DateTimeOffset ExpiresAt);

public static class EnrollmentEndpoints
{
    /// <summary>Roles a device may ask for. Phase 1 grants only "operator".</summary>
    private static readonly HashSet<string> AllowedEnrollmentRoles = new(StringComparer.Ordinal) { "operator" };

    public static IEndpointRouteBuilder MapEnrollmentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/devices/enroll", HandleAsync)
            .WithName("EnrollDevice")
            .RequireRateLimiting("enrollment")
            .Accepts<EnrollRequest>("application/json")
            .Produces(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        return endpoints;
    }

    private static async Task HandleAsync(
        HttpContext context,
        EnrollRequest request,
        DataHubRuntimeOptions options,
        ILicenseAssertionValidator validator,
        EnrollmentRepository repository)
    {
        if (request is null)
        {
            await ApiProblemWriter.WriteAsync(context, StatusCodes.Status422UnprocessableEntity, "VALIDATION_FAILED", "The enrollment request is required.");
            return;
        }
        var header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            await ApiProblemWriter.WriteAsync(context, StatusCodes.Status401Unauthorized, ApiProblemCodes.Unauthorized, "A signed license assertion is required.");
            return;
        }

        var assertion = header["Bearer ".Length..].Trim();
        var validation = await validator.ValidateAsync(assertion, context.RequestAborted);
        if (!validation.Succeeded || validation.Identity is null)
        {
            var status = validation.FailureCode == "LICENSE_ASSERTION_UNAVAILABLE"
                ? StatusCodes.Status503ServiceUnavailable
                : validation.FailureCode == ApiProblemCodes.ChannelMismatch
                ? StatusCodes.Status403Forbidden
                : StatusCodes.Status401Unauthorized;
            var code = validation.FailureCode == "LICENSE_ASSERTION_UNAVAILABLE"
                ? ApiProblemCodes.ServiceUnavailable
                : validation.FailureCode == ApiProblemCodes.ChannelMismatch
                ? ApiProblemCodes.ChannelMismatch
                : ApiProblemCodes.Unauthorized;
            await ApiProblemWriter.WriteAsync(context, status, code, status == StatusCodes.Status503ServiceUnavailable
                ? "The production license verifier is not enabled on this backend yet."
                : "The signed license assertion is invalid, expired, or belongs to another deployment channel.");
            return;
        }
        if (!string.Equals(validation.Identity.Channel, options.Channel, StringComparison.Ordinal))
        {
            await ApiProblemWriter.WriteAsync(context, StatusCodes.Status403Forbidden, ApiProblemCodes.ChannelMismatch, "The license channel does not match this deployment.");
            return;
        }
        if (string.IsNullOrWhiteSpace(request.SiteCode) || string.IsNullOrWhiteSpace(request.DeviceName))
        {
            await ApiProblemWriter.WriteAsync(context, StatusCodes.Status422UnprocessableEntity, "VALIDATION_FAILED", "siteCode and deviceName are required.");
            return;
        }

        var siteCode = request.SiteCode.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(siteCode) || !validation.Identity.SiteCodes.Contains(siteCode))
        {
            await ApiProblemWriter.WriteAsync(context, StatusCodes.Status403Forbidden, ApiProblemCodes.SiteNotLicensed, "The requested site is outside the signed license scope.");
            return;
        }

        // Allowlist, not pass-through: the client must not be able to name its own role. The
        // repository is still the authority (it rejects anything but "operator"), this only
        // makes the intent explicit at the edge and keeps casing from causing a spurious 422.
        var requestedRole = (request.Role ?? "").Trim().ToLowerInvariant();
        if (requestedRole.Length == 0) requestedRole = "operator";
        if (!AllowedEnrollmentRoles.Contains(requestedRole))
        {
            await ApiProblemWriter.WriteAsync(context, StatusCodes.Status422UnprocessableEntity, "VALIDATION_FAILED", "role must be 'operator'.");
            return;
        }

        var result = await repository.EnrollAsync(
            siteCode,
            request.DeviceName.Trim(),
            requestedRole,
            validation.Identity,
            context.RequestAborted);
        if (!result.Succeeded)
        {
            await ApiProblemWriter.WriteAsync(context, result.StatusCode, result.ProblemCode ?? ApiProblemCodes.ServiceUnavailable, result.Detail ?? "Enrollment failed.");
            return;
        }

        context.Response.StatusCode = StatusCodes.Status201Created;
        await context.Response.WriteAsJsonAsync(new EnrollmentResponse(
            result.DeviceId!.Value,
            result.SiteId!.Value,
            result.SiteCode!,
            options.Channel,
            "Bearer",
            result.DeviceToken!,
            result.TokenVersion,
            result.ExpiresAt!.Value), context.RequestAborted);
    }
}
