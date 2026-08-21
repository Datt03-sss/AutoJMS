using System.Text.Json.Serialization;
using System.Text.Json;

namespace AutoJMS.DataHub.Api.Infrastructure;

public sealed record ApiProblem(
    string Type,
    string Title,
    int Status,
    string Code,
    string Detail,
    string TraceId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Instance = null);

public static class ApiProblemWriter
{
    public static Task WriteAsync(HttpContext context, int status, string code, string detail)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        var problem = new ApiProblem(
            $"https://datahub.example.com/problems/{code.ToLowerInvariant().Replace('_', '-')}",
            code.Replace('_', ' '),
            status,
            code,
            detail,
            context.TraceIdentifier);
        return context.Response.WriteAsync(JsonSerializer.Serialize(problem), context.RequestAborted);
    }
}
