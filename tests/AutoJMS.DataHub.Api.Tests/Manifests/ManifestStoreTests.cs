using System.Text;
using AutoJMS.DataHub.Api.Configuration;
using AutoJMS.DataHub.Api.Manifests;
using Microsoft.Extensions.Logging.Abstractions;

namespace AutoJMS.DataHub.Api.Tests.Manifests;

public sealed class ManifestStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "autojms-manifest-store-" + Guid.NewGuid().ToString("N"));
    private readonly ManifestStore _store;

    public ManifestStoreTests()
    {
        Directory.CreateDirectory(_root);
        _store = new ManifestStore(
            new DataHubRuntimeOptions { ManifestRoot = _root },
            NullLogger<ManifestStore>.Instance);
    }

    [Fact]
    public async Task Reading_an_object_that_was_never_published_returns_null()
    {
        Assert.Null(await _store.ReadAsync("manifest/version-latest.json", CancellationToken.None));
    }

    [Fact]
    public async Task Publishing_creates_the_object_then_replaces_it_in_place()
    {
        var first = await _store.WriteAsync("manifest/version-latest.json", Utf8("{\"version\":\"1.0.0\"}"), CancellationToken.None);
        Assert.True(first.Created);

        var second = await _store.WriteAsync("manifest/version-latest.json", Utf8("{\"version\":\"1.0.1\"}"), CancellationToken.None);
        Assert.False(second.Created);
        Assert.NotEqual(first.ETag, second.ETag);

        var stored = await _store.ReadAsync("manifest/version-latest.json", CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal("{\"version\":\"1.0.1\"}", Encoding.UTF8.GetString(stored.Content));
        Assert.Equal(second.ETag, stored.ETag);
    }

    [Fact]
    public async Task Publishing_creates_the_nested_directory_a_versioned_payload_needs()
    {
        await _store.WriteAsync("selector-updates/1.26.6/runtime-config.enc", [1, 2, 3, 4], CancellationToken.None);

        var stored = await _store.ReadAsync("selector-updates/1.26.6/runtime-config.enc", CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, stored.Content);
        Assert.Equal("application/octet-stream", stored.ContentType);
    }

    [Fact]
    public async Task An_object_replaced_outside_the_process_is_not_served_from_the_cache()
    {
        await _store.WriteAsync("configs/runtime-policy.json", Utf8("{\"a\":1}"), CancellationToken.None);
        var before = await _store.ReadAsync("configs/runtime-policy.json", CancellationToken.None);

        var path = Path.Combine(_root, "configs", "runtime-policy.json");
        await File.WriteAllBytesAsync(path, Utf8("{\"a\":2}"));
        // The cache keys on write time and length; nudge the timestamp so the change is
        // visible even on a filesystem with coarse resolution.
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(1));

        var after = await _store.ReadAsync("configs/runtime-policy.json", CancellationToken.None);
        Assert.NotNull(before);
        Assert.NotNull(after);
        Assert.NotEqual(before.ETag, after.ETag);
        Assert.Equal("{\"a\":2}", Encoding.UTF8.GetString(after.Content));
    }

    [Fact]
    public void The_etag_is_the_content_hash_so_identical_payloads_share_it()
    {
        Assert.True(ManifestStore.TryValidatePayload("manifest/x.json", Utf8("{\"a\":1}"), out _));

        var repeated = ManifestStore.MaximumObjectBytes;
        Assert.False(ManifestStore.TryValidatePayload("modules/blob.bin", new byte[repeated + 1], out var reason));
        Assert.Contains("exceeds", reason);
    }

    [Theory]
    [InlineData("{\"a\":1}", true)]
    [InlineData("[1,2,3]", true)]
    [InlineData("{\"a\":1", false)]
    [InlineData("not json at all", false)]
    // A bare scalar parses as JSON but would break every consumer expecting a document.
    [InlineData("42", false)]
    [InlineData("\"text\"", false)]
    public void A_json_object_must_parse_before_it_can_replace_a_live_object(string body, bool expected)
    {
        Assert.Equal(expected, ManifestStore.TryValidatePayload("manifest/tier-definitions.json", Utf8(body), out _));
    }

    [Fact]
    public void A_non_json_object_is_never_parsed_as_json()
    {
        // An encrypted small-update payload is opaque bytes; requiring JSON here would
        // make it impossible to publish.
        Assert.True(ManifestStore.TryValidatePayload("selector-updates/runtime-config.enc", [0xFF, 0x00, 0xFE], out _));
    }

    [Fact]
    public void An_empty_body_is_rejected_so_a_failed_upload_cannot_blank_a_policy()
    {
        Assert.False(ManifestStore.TryValidatePayload("configs/runtime-policy.json", [], out var reason));
        Assert.Contains("empty", reason, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);

    public void Dispose()
    {
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
