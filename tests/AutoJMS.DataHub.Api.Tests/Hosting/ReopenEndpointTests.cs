using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AutoJMS.DataHub.Api.Tests.Hosting;

/// <summary>
/// The route's placement is the thing under test. The contract put reopen at
/// <c>/api/v1/sites/...</c>, where AdminAuthenticationMiddleware returns early and never
/// sets the marker the handler reads — so the endpoint would have answered 401 to a
/// correctly authenticated operator, forever. The third and fourth tests below fail if the
/// route moves back outside the admin prefix: an authenticated call must reach the handler
/// to produce a 400, whereas only the middleware was needed to produce the 401s above it.
///
/// Everything here stops before the repository, so none of it needs a database.
/// </summary>
public sealed class ReopenEndpointTests : IDisposable
{
    private const string AdminToken = "test-admin-token-that-is-long-enough-32";

    private readonly WebApplicationFactory<Program> _factory;

    public ReopenEndpointTests()
    {
        // DATAHUB_ADMIN_TOKEN must be set. AdminAuthenticationMiddleware checks it BEFORE
        // the bearer token (AdminAuthenticationMiddleware.cs:55) and answers 503 "not
        // configured" when it is absent — so without this setting every assertion below
        // would be measuring a missing configuration rather than the guard.
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseSetting(WebHostDefaults.EnvironmentKey, "Testing")
            .UseSetting("DATAHUB_ADMIN_TOKEN", AdminToken));
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task The_route_exists_under_the_admin_prefix_and_is_closed_without_a_token()
    {
        using var client = _factory.CreateClient();
        using var request = Request(Route(), "key-12345678");

        using var response = await client.SendAsync(request);

        // 401, not 404: the route is mapped and the guard closed it. A 404 here means the
        // endpoint is not registered; a 200 means it is not guarded.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_wrong_bearer_token_is_rejected()
    {
        // The whole reason the route moved under /api/v1/admin rather than having the
        // handler check a capability: an operator action must not be reachable with a
        // station's credentials.
        using var client = _factory.CreateClient();
        using var request = Request(Route(), "key-12345678");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "a-device-token-that-is-not-the-admin-token");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_authenticated_operator_reaches_the_handler_and_a_missing_key_is_rejected()
    {
        // The counterpart to the two 401s: with the right token the request gets past the
        // middleware and into the handler, which rejects the missing header itself. Without
        // this case, all three tests above would still pass if the route were never mapped
        // at all — 401 can come from the middleware alone.
        using var client = _factory.CreateClient();
        using var request = Request(Route(), idempotencyKey: null);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_short_idempotency_key_is_rejected()
    {
        using var client = _factory.CreateClient();
        using var request = Request(Route(), "short");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        using var response = await client.SendAsync(request);

        // Rejected before the repository is reached, which is what lets this run with no
        // database: the handler validates the header, then hands off.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static string Route()
        => $"/api/v1/admin/sites/{Guid.NewGuid():D}/waybills/JMS-1/reopen";

    private static HttpRequestMessage Request(string path, string? idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (idempotencyKey is not null)
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }
}
