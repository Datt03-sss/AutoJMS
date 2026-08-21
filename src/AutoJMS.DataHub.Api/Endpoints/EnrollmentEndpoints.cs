using AutoJMS.DataHub.Api.Auth;
using AutoJMS.DataHub.Api.Configuration;
using AutoJMS.DataHub.Api.Infrastructure;

namespace AutoJMS.DataHub.Api.Endpoints;

public sealed record EnrollRequest(string SiteCode, string DeviceName, string? Role);

public static class EnrollmentEndpoints
{
    public static IEndpointRouteBuilder MapEnrollmentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/devices/enroll", HandleAsync)
            .WithName("EnrollDevice")
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
        ILicenseAssertionValidator validator)
    {
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
            await ApiProblemWriter.WriteAsync(context, StatusCodes.Status401Unauthorized, ApiProblemCodes.Unauthorized, "The signed license assertion is invalid or expired.");
            return;
        }
        if (!string.Equals(validation.Identity.Channel, options.Channel, StringComparison.Ordinal))
        {
            await ApiProblemWriter.WriteAsync(context, StatusCodes.Status403Forbidden, ApiProblemCodes.ChannelMismatch, "The license channel does not match this deployment.");
            return;
        }
        if (string.IsNullOrWhiteSpace(request.SiteCode) || !validation.Identity.SiteCodes.Contains(request.SiteCode.Trim()))
        {
            await ApiProblemWriter.WriteAsync(context, StatusCodes.Status403Forbidden, ApiProblemCodes.SiteNotLicensed, "The requested site is outside the signed license scope.");
            return;
        }

        // Site provisioning and device persistence are deliberately owned by the
        // Task 4 repository. Returning 503 here prevents a false enrollment while
        // keeping the auth boundary executable in staging tests.
        await ApiProblemWriter.WriteAsync(context, StatusCodes.Status503ServiceUnavailable, ApiProblemCodes.ServiceUnavailable, "Enrollment storage is not enabled in the API skeleton.");
    }
}
