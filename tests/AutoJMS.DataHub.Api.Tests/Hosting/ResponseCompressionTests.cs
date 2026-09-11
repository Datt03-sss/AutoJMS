using System.Net;
using System.Net.Http.Json;
using AutoJMS.DataHub.Api.Endpoints;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AutoJMS.DataHub.Api.Tests.Hosting;

/// <summary>
/// Compression is on for the bulk JSON routes and off for the two that hand back a
/// credential. Getting that backwards is not a visible failure — the responses still
/// parse, the tests still pass, and the only symptom is that a device token's length
/// becomes measurable through the compressed size (BREACH/CRIME). So the exclusion has
/// to be asserted from out here: nothing inside Program.cs can tell you which branch a
/// request took.
/// </summary>
public sealed class ResponseCompressionTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ResponseCompressionTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
            builder.UseSetting(WebHostDefaults.EnvironmentKey, "Testing"));
    }

    /// <summary>
    /// TestServer's handler does not decompress, so Content-Encoding survives to the
    /// assertion. Accept-Encoding has to be explicit — the middleware does nothing
    /// without it, which would make every assertion below pass for the wrong reason.
    /// </summary>
    private static HttpRequestMessage GzipRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.AcceptEncoding.ParseAdd("gzip");
        return request;
    }

    [Fact]
    public async Task Site_data_responses_are_compressed()
    {
        using var client = _factory.CreateClient();
        using var request = GzipRequest(HttpMethod.Get, "/health/live");

        using var response = await client.SendAsync(request);

        // The control for the two exclusion tests. Without it, a typo that disabled
        // compression everywhere would leave this whole file green.
        Assert.Contains("gzip", response.Content.Headers.ContentEncoding);
    }

    [Fact]
    public async Task Enrollment_responses_are_never_compressed()
    {
        using var client = _factory.CreateClient();
        using var request = GzipRequest(HttpMethod.Post, "/api/v1/devices/enroll");
        request.Content = JsonContent.Create(new EnrollRequest("SITE-A1", "PC-01", "operator"));

        using var response = await client.SendAsync(request);

        // 401 rather than a minted token, because no assertion is attached — but it is
        // the same branch of the pipeline, and the same compressible content type, that
        // the 201 carrying the device token would take.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Empty(response.Content.Headers.ContentEncoding);
    }

    [Fact]
    public async Task Hub_responses_are_never_compressed()
    {
        using var client = _factory.CreateClient();
        using var request = GzipRequest(HttpMethod.Get, "/hubs/site/negotiate");

        using var response = await client.SendAsync(request);

        // negotiate hands out a connection token, and the compressor's buffering would
        // also sit across long-polling and SSE frames that are supposed to flush as
        // they are written.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Empty(response.Content.Headers.ContentEncoding);
    }
}
