using AutoJMS.DataHub.Api.Manifests;

namespace AutoJMS.DataHub.Api.Tests.Manifests;

/// <summary>
/// The control-plane read route is anonymous, so this validator is the whole
/// perimeter. Every rejection below is a way a public GET could otherwise reach a
/// file the API never meant to publish.
/// </summary>
public sealed class ManifestObjectPathTests
{
    [Theory]
    [InlineData("manifest/version-latest.json")]
    [InlineData("manifest/tier-definitions.json")]
    [InlineData("manifest/hash-manifest.json")]
    [InlineData("configs/runtime-policy.json")]
    [InlineData("configs/runtime-policy.ultra.json")]
    [InlineData("configs/public-config.json")]
    [InlineData("selector-updates/selector-update-manifest.json")]
    [InlineData("selector-updates/1.26.6/runtime-config.enc")]
    [InlineData("selector-updates/1.26.6/runtime-config.enc.sig")]
    [InlineData("modules/modules.json")]
    public void Accepts_every_object_path_the_desktop_and_release_script_already_request(string candidate)
    {
        var accepted = ManifestObjectPath.TryCanonicalize(candidate, out var canonical, out var reason);

        Assert.True(accepted, $"'{candidate}' must be publishable but was rejected: {reason}");
        Assert.Equal(candidate, canonical);
    }

    [Theory]
    [InlineData("/manifest/version-latest.json")]
    [InlineData("  manifest/version-latest.json  ")]
    public void Strips_a_leading_slash_and_surrounding_whitespace(string candidate)
    {
        Assert.True(ManifestObjectPath.TryCanonicalize(candidate, out var canonical, out _));
        Assert.Equal("manifest/version-latest.json", canonical);
    }

    [Theory]
    [InlineData("manifest/../../etc/passwd")]
    [InlineData("manifest/..%2fsecret.json")]
    [InlineData("configs/../configs/runtime-policy.json")]
    [InlineData("manifest\\version-latest.json")]
    [InlineData("manifest//version-latest.json")]
    [InlineData("C:/secrets/keys.json")]
    [InlineData("https://evil.example.com/x.json")]
    public void Rejects_traversal_and_absolute_or_absolute_looking_paths(string candidate)
    {
        Assert.False(ManifestObjectPath.TryCanonicalize(candidate, out var canonical, out var reason));
        Assert.Null(canonical);
        Assert.NotEmpty(reason);
    }

    [Theory]
    [InlineData("secrets/device-token.json")]
    // Case-sensitive on purpose: the objects live on a case-sensitive filesystem, so
    // accepting these would create a second, silently empty namespace.
    [InlineData("Manifest/version-latest.json")]
    [InlineData("MANIFEST/version-latest.json")]
    [InlineData("manifests/version-latest.json")]
    public void Rejects_any_container_outside_the_allowlist(string candidate)
    {
        Assert.False(ManifestObjectPath.TryCanonicalize(candidate, out _, out var reason));
        Assert.Contains("container", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_a_bare_file_name_with_no_container_at_all()
    {
        Assert.False(ManifestObjectPath.TryCanonicalize("appsettings.json", out _, out var reason));
        // The container check runs before the depth check, so the reason names the real
        // problem instead of implying that a deeper path would be accepted.
        Assert.Contains("container", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_a_bare_container_so_the_route_can_never_list_a_directory()
    {
        Assert.False(ManifestObjectPath.TryCanonicalize("manifest", out _, out _));
        Assert.False(ManifestObjectPath.TryCanonicalize("manifest/", out _, out _));
    }

    [Theory]
    [InlineData("manifest/.hidden.json")]
    [InlineData("manifest/trailing-")]
    [InlineData("manifest/-leading.json")]
    // Ends with 'n', so a first/last-character check alone accepts it. The stem's
    // dangling hyphen is what makes it not a name the publisher would ever produce.
    [InlineData("manifest/trailing-.json")]
    [InlineData("manifest/stem.-part.json")]
    [InlineData("manifest/stem..json")]
    [InlineData("manifest/name with space.json")]
    [InlineData("manifest/name;rm.json")]
    [InlineData("manifest/name?query=1")]
    [InlineData("manifest/name#fragment")]
    public void Rejects_segments_that_are_not_plain_names(string candidate)
    {
        Assert.False(ManifestObjectPath.TryCanonicalize(candidate, out _, out var reason));
        Assert.NotEmpty(reason);
    }

    [Fact]
    public void Rejects_a_path_deeper_than_four_segments()
    {
        Assert.True(ManifestObjectPath.TryCanonicalize("selector-updates/a/b/c.enc", out _, out _));
        Assert.False(ManifestObjectPath.TryCanonicalize("selector-updates/a/b/c/d.enc", out _, out _));
    }

    [Fact]
    public void Rejects_an_over_long_path_and_an_over_long_segment()
    {
        var longSegment = new string('a', 81);
        Assert.False(ManifestObjectPath.TryCanonicalize($"manifest/{longSegment}.json", out _, out _));

        var longPath = "manifest/" + new string('a', ManifestObjectPath.MaximumLength);
        Assert.False(ManifestObjectPath.TryCanonicalize(longPath, out _, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/")]
    public void Rejects_an_empty_path(string? candidate)
    {
        Assert.False(ManifestObjectPath.TryCanonicalize(candidate, out _, out var reason));
        Assert.NotEmpty(reason);
    }

    [Fact]
    public void Only_json_objects_are_validated_as_json_and_served_as_json()
    {
        Assert.True(ManifestObjectPath.RequiresJsonBody("manifest/version-latest.json"));
        Assert.False(ManifestObjectPath.RequiresJsonBody("selector-updates/runtime-config.enc"));

        Assert.Equal("application/json; charset=utf-8", ManifestObjectPath.ContentTypeFor("manifest/version-latest.json"));
        // An opaque payload must never come back as something a browser renders.
        Assert.Equal("application/octet-stream", ManifestObjectPath.ContentTypeFor("selector-updates/runtime-config.enc"));
    }
}
