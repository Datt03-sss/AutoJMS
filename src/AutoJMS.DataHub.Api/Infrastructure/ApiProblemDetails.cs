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

    /// <summary>
    /// Prefix of the RFC 7807 <c>type</c> URI. Empty means "emit a relative reference".
    ///
    /// This used to be the literal <c>https://datahub.example.com</c>, baked into every
    /// error response the API has ever sent. That is wrong twice over: nobody owns that
    /// domain, and staging and production are two different hosts, so no single constant
    /// could have been right for both.
    ///
    /// A static rather than an injected option because <see cref="WriteAsync"/> is called
    /// from middleware and from 24 endpoint sites, several of which run before any DI
    /// scope exists. It is written exactly once, from Program.cs, before the first
    /// request is served.
    /// </summary>
    private static string _problemTypeBaseUri = "";

    /// <summary>Set once at startup from DATAHUB_PUBLIC_HOST. A blank or RFC 2606 host leaves relative URIs in place.</summary>
    public static void ConfigureProblemTypeBaseUri(string publicHost)
        => _problemTypeBaseUri = string.IsNullOrWhiteSpace(publicHost) ? "" : $"https://{publicHost}";

    /// <summary>
    /// Relative when unconfigured. RFC 7807 §3.1 permits it and requires the client to
    /// resolve it against the request URI, so an operator who forgot to set the host gets
    /// a <c>type</c> that still points at the right deployment — strictly better than an
    /// absolute URI that points at someone else's.
    /// </summary>
    internal static string ProblemType(string code)
        => $"{_problemTypeBaseUri}/problems/{code.ToLowerInvariant().Replace('_', '-')}";

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
            ProblemType(code),
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
