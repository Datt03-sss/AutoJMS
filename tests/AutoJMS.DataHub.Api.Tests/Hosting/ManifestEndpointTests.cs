using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AutoJMS.DataHub.Api.Tests.Hosting;

/// <summary>
/// End-to-end checks on the control plane through the real middleware pipeline.
/// The desktop reads these objects anonymously and the release script publishes
/// them with the operator token, so both halves have to hold with no database.
/// </summary>
public sealed class ManifestEndpointTests : IDisposable
{
    private const string AdminToken = "test-admin-token-that-is-long-enough-32";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "autojms-manifest-api-" + Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<Program> _factory;

    public ManifestEndpointTests()
    {
        Directory.CreateDirectory(_root);
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseSetting(WebHostDefaults.EnvironmentKey, "Testing")
            .UseSetting("DATAHUB_MANIFEST_ROOT", _root)
            .UseSetting("DATAHUB_ADMIN_TOKEN", AdminToken));
    }

    [Fact]
    public async Task An_unpublished_object_is_a_problem_json_404_not_a_401()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/manifest/version-latest.json");

        // 401 here is the bug this route exists to fix: the desktop sends no
        // credentials and would silently fall back to safe-default BASE.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_published_object_is_readable_with_no_credentials_at_all()
    {
        using var client = _factory.CreateClient();
        await PublishAsync(client, "manifest/tier-definitions.json", "{\"schemaVersion\":2}");

        var response = await client.GetAsync("/manifest/tier-definitions.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("{\"schemaVersion\":2}", await response.Content.ReadAsStringAsync());
        Assert.NotNull(response.Headers.ETag);
    }

    [Fact]
    public async Task A_published_object_answers_HEAD_so_its_presence_can_be_checked_without_downloading_it()
    {
        using var client = _factory.CreateClient();
        await PublishAsync(client, "configs/runtime-policy.ultra.json", "{\"tier\":\"ULTRA\"}");

        using var request = new HttpRequestMessage(HttpMethod.Head, "/configs/runtime-policy.ultra.json");
        var response = await client.SendAsync(request);

        // MapGet alone answered 405 here, which reads exactly like a broken reverse
        // proxy when the object is in fact published and being served over GET.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Headers.ETag);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task An_opaque_payload_is_served_as_octet_stream()
    {
        using var client = _factory.CreateClient();
        await PublishBytesAsync(client, "selector-updates/1.26.6/runtime-config.enc", [0x00, 0x01, 0x02]);

        var response = await client.GetAsync("/selector-updates/1.26.6/runtime-config.enc");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(new byte[] { 0x00, 0x01, 0x02 }, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task An_unchanged_object_answers_304_so_a_restart_storm_costs_no_bandwidth()
    {
        using var client = _factory.CreateClient();
        await PublishAsync(client, "configs/runtime-policy.json", "{\"tier\":\"BASE\"}");

        var first = await client.GetAsync("/configs/runtime-policy.json");
        var etag = first.Headers.ETag!.ToString();

        using var conditional = new HttpRequestMessage(HttpMethod.Get, "/configs/runtime-policy.json");
        conditional.Headers.TryAddWithoutValidation("If-None-Match", etag);
        var second = await client.SendAsync(conditional);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
        Assert.Empty(await second.Content.ReadAsByteArrayAsync());
    }

    [Theory]
    [InlineData("/appsettings.json")]
    [InlineData("/manifest")]
    [InlineData("/manifests/version-latest.json")]
    [InlineData("/configs/../appsettings.json")]
    [InlineData("/manifest/..%2fappsettings.json")]
    public async Task Nothing_outside_the_published_containers_is_ever_served(string path)
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Publishing_without_the_operator_token_is_rejected()
    {
        using var client = _factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/admin/manifests/manifest/version-latest.json")
        {
            Content = JsonBody("{\"version\":\"1.0.0\"}")
        };
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(await ReadRawAsync("manifest/version-latest.json"));
    }

    [Fact]
    public async Task A_wrong_operator_token_is_rejected_and_writes_nothing()
    {
        using var client = _factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/admin/manifests/manifest/version-latest.json")
        {
            Content = JsonBody("{\"version\":\"1.0.0\"}")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken + "x");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(await ReadRawAsync("manifest/version-latest.json"));
    }

    [Fact]
    public async Task Publishing_returns_201_the_first_time_and_200_on_replacement()
    {
        using var client = _factory.CreateClient();

        var created = await PublishAsync(client, "manifest/hash-manifest.json", "{\"a\":1}");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var replaced = await PublishAsync(client, "manifest/hash-manifest.json", "{\"a\":2}");
        Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);

        Assert.Equal("{\"a\":2}", await ReadRawAsync("manifest/hash-manifest.json"));
    }

    [Theory]
    [InlineData("secrets/keys.json")]
    [InlineData("manifest/../appsettings.json")]
    [InlineData("manifest")]
    public async Task A_path_outside_the_allowlist_cannot_be_published(string objectPath)
    {
        using var client = _factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/admin/manifests/" + objectPath)
        {
            Content = JsonBody("{\"a\":1}")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var response = await client.SendAsync(request);

        Assert.True(
            response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound,
            $"Expected a rejection for '{objectPath}' but got {(int)response.StatusCode}.");
    }

    [Fact]
    public async Task A_corrupt_json_payload_cannot_replace_a_working_one()
    {
        using var client = _factory.CreateClient();
        await PublishAsync(client, "configs/public-config.json", "{\"good\":true}");

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/admin/manifests/configs/public-config.json")
        {
            Content = JsonBody("{\"good\":true")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("{\"good\":true}", await ReadRawAsync("configs/public-config.json"));
    }

    [Fact]
    public async Task Administrative_publishing_is_closed_when_no_operator_token_is_configured()
    {
        using var unconfigured = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseSetting(WebHostDefaults.EnvironmentKey, "Testing")
            .UseSetting("DATAHUB_MANIFEST_ROOT", _root)
            .UseSetting("DATAHUB_ADMIN_TOKEN", ""));
        using var client = unconfigured.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/admin/manifests/manifest/version-latest.json")
        {
            Content = JsonBody("{\"version\":\"1.0.0\"}")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        var response = await client.SendAsync(request);

        // Closed, not open: an unconfigured host must never accept a publish.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private Task<HttpResponseMessage> PublishAsync(HttpClient client, string objectPath, string json)
        => PublishContentAsync(client, objectPath, JsonBody(json));

    private Task<HttpResponseMessage> PublishBytesAsync(HttpClient client, string objectPath, byte[] content)
    {
        var body = new ByteArrayContent(content);
        body.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return PublishContentAsync(client, objectPath, body);
    }

    private async Task<HttpResponseMessage> PublishContentAsync(HttpClient client, string objectPath, HttpContent content)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/admin/manifests/" + objectPath)
        {
            Content = content
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        return await client.SendAsync(request);
    }

    private async Task<string?> ReadRawAsync(string objectPath)
    {
        var path = Path.Combine(_root, objectPath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) ? await File.ReadAllTextAsync(path) : null;
    }

    private static StringContent JsonBody(string json) => new(json, Encoding.UTF8, "application/json");

    public void Dispose()
    {
        _factory.Dispose();
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory does not affect the next run.
        }
    }
}
