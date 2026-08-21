using System.Text.Json.Serialization;
using System.Text.Json;

namespace AutoJMS.DataHub.Api.Infrastructure;

public sealed record ApiProblem(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("status")] int Status,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("detail")] string Detail,
    [property: JsonPropertyName("traceId")] string TraceId,
    [property: JsonPropertyName("instance")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Instance = null);

public static class ApiProblemWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IResult Result(int status, string code, string detail)
        => new ApiProblemResult(status, code, detail);

    public static Task WriteAsync(HttpContext context, int status, string code, string detail)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        if (status == StatusCodes.Status401Unauthorized)
            context.Response.Headers.WWWAuthenticate = "Bearer";
        if (status == StatusCodes.Status503ServiceUnavailable)
            context.Response.Headers.RetryAfter = "60";
        var problem = new ApiProblem(
            $"https://datahub.example.com/problems/{code.ToLowerInvariant().Replace('_', '-')}",
            code.Replace('_', ' '),
            status,
            code,
            detail,
            context.TraceIdentifier);
        return context.Response.WriteAsync(JsonSerializer.Serialize(problem, JsonOptions), context.RequestAborted);
    }

    private sealed class ApiProblemResult(int status, string code, string detail) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
            => WriteAsync(httpContext, status, code, detail);
    }
}
