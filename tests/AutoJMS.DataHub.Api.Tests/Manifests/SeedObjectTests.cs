using System.Text.Json;
using AutoJMS.DataHub.Api.Manifests;

namespace AutoJMS.DataHub.Api.Tests.Manifests;

/// <summary>
/// Guards <c>backend/datahub/seeds</c> — the objects publish-manifests.sh/.ps1 upload
/// to a fresh VPS.
///
/// These are data files, so nothing else in the build would notice a mistake in them,
/// and every mistake they can carry is silent at runtime: a misspelled filename, a
/// container outside the allowlist, or a stray <c>false</c> in the shared policy all
/// produce exactly one symptom — ULTRA stations quietly running as BASE, with no error
/// logged anywhere on the server.
/// </summary>
public sealed class SeedObjectTests
{
    /// <summary>
    /// The feature keys <c>TierRuntimePolicy.Resolve(RuntimePolicyDocument, tier)</c>
    /// ANDs with the license entitlement. A published <c>true</c> cannot grant any of
    /// them — only the license can — so the only thing a policy file can do with these
    /// is take them away.
    /// </summary>
    private static readonly string[] TierGateKeys =
    [
        "forms.fullStackOperation",
        "fullStack.backgroundSync",
        "fullStack.inventorySync",
        "fullStack.databaseTracking",
        "tabs.tracking",
        "tabs.print"
    ];

    /// <summary>
    /// Substrings that must never appear in a property name. The seeds are served
    /// anonymously to every station, so a key placed here is a published key.
    /// Deliberately not matched against values: "TokenBroker" is a legitimate one.
    /// </summary>
    private static readonly string[] SecretishNames =
    [
        "secret", "password", "passwd", "privatekey", "apikey", "credential", "pepper", "signingkey"
    ];

    [Fact]
    public void Every_seed_file_is_publishable_through_the_admin_route()
    {
        foreach (var (objectPath, file) in EnumerateSeeds())
        {
            Assert.True(
                ManifestObjectPath.TryCanonicalize(objectPath, out var canonical, out var reason),
                $"seeds/{objectPath} cannot be published: {reason}");
            Assert.Equal(objectPath, canonical);

            var content = File.ReadAllBytes(file);
            Assert.True(
                ManifestStore.TryValidatePayload(canonical!, content, out var payloadReason),
                $"seeds/{objectPath} would be rejected by the publish route: {payloadReason}");
        }
    }

    [Fact]
    public void The_policy_files_the_client_asks_for_first_exist_under_exactly_those_names()
    {
        // VpsRuntimePolicyService builds these paths by string interpolation on the
        // lowercased tier. A rename or a typo here is not a 500 or a failed publish;
        // it is a 404 that ends in SafeDefault("BASE").
        var required = new[]
        {
            "configs/runtime-policy.base.json",
            "configs/runtime-policy.ultra.json",
            "configs/runtime-policy.json",
            "manifest/tier-definitions.json"
        };

        var present = EnumerateSeeds().Select(seed => seed.ObjectPath).ToHashSet(StringComparer.Ordinal);
        foreach (var path in required)
            Assert.Contains(path, present);
    }

    [Theory]
    [InlineData("configs/runtime-policy.ultra.json")]
    [InlineData("configs/runtime-policy.json")]
    public void The_ultra_and_shared_policies_impose_no_restriction(string objectPath)
    {
        var features = ReadFeatures(objectPath);

        foreach (var key in TierGateKeys)
        {
            Assert.True(features.TryGetValue(key, out var value), $"{objectPath} is missing '{key}'.");
            // false here is a fleet-wide kill switch, and for the shared file it hits
            // every tier that has no file of its own — including a future ULTRA-class
            // tier name. If one is ever wanted deliberately, this assertion is the
            // place to say so.
            Assert.True(value, $"{objectPath} sets '{key}' to false, which downgrades every ULTRA station reading it.");
        }
    }

    [Fact]
    public void The_base_policy_keeps_manual_tracking_and_print()
    {
        var features = ReadFeatures("configs/runtime-policy.base.json");

        // BASE's entitlement already denies the fullStack keys, so their value here is
        // documentation. These two are the ones BASE really has, and a false would take
        // away the whole of what a BASE licence bought.
        Assert.True(features["tabs.tracking"]);
        Assert.True(features["tabs.print"]);
    }

    [Fact]
    public void Each_policy_declares_the_tier_it_is_for_and_the_shared_one_declares_none()
    {
        // TryParsePolicy rejects a document whose non-empty tier differs from the tier
        // being requested. That rejection is what stops runtime-policy.base.json from
        // reaching an ULTRA station — and an empty tier is what lets the shared file
        // serve every tier instead of only BASE.
        Assert.Equal("BASE", ReadTier("configs/runtime-policy.base.json"));
        Assert.Equal("ULTRA", ReadTier("configs/runtime-policy.ultra.json"));
        Assert.Equal("", ReadTier("configs/runtime-policy.json"));
    }

    [Fact]
    public void No_seed_object_carries_anything_shaped_like_a_secret()
    {
        foreach (var (objectPath, file) in EnumerateSeeds())
        {
            if (!objectPath.EndsWith(".json", StringComparison.Ordinal))
                continue;

            using var document = JsonDocument.Parse(File.ReadAllBytes(file));
            foreach (var name in EnumeratePropertyNames(document.RootElement))
            {
                var lowered = name.ToLowerInvariant();
                foreach (var forbidden in SecretishNames)
                    Assert.False(
                        lowered.Contains(forbidden, StringComparison.Ordinal),
                        $"seeds/{objectPath} has a property named '{name}'. These objects are served to anonymous callers.");
            }
        }
    }

    private static IEnumerable<string> EnumeratePropertyNames(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    yield return property.Name;
                    foreach (var nested in EnumeratePropertyNames(property.Value))
                        yield return nested;
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    foreach (var nested in EnumeratePropertyNames(item))
                        yield return nested;
                break;
        }
    }

    private static Dictionary<string, bool> ReadFeatures(string objectPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(SeedPath(objectPath)));
        var features = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        if (!document.RootElement.TryGetProperty("features", out var element))
            return features;

        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                features[property.Name] = property.Value.GetBoolean();
        }

        return features;
    }

    private static string ReadTier(string objectPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(SeedPath(objectPath)));
        return document.RootElement.TryGetProperty("tier", out var tier) ? tier.GetString() ?? "" : "";
    }

    private static string SeedPath(string objectPath)
    {
        var path = Path.Combine(SeedRoot(), objectPath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"seeds/{objectPath} does not exist.");
        return path;
    }

    /// <summary>
    /// Every seed with the object path it will be published under. The directory
    /// layout *is* the object path, which is the contract publish-manifests relies on.
    /// </summary>
    private static IEnumerable<(string ObjectPath, string FullPath)> EnumerateSeeds()
    {
        var root = SeedRoot();
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(file);
            // Mirrors the exclusions in publish-manifests.sh/.ps1.
            if (name == "README.md" || name.StartsWith('.'))
                continue;

            yield return (Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/'), file);
        }
    }

    private static string SeedRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AutoJMS.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        var root = Path.Combine(directory!.FullName, "backend", "datahub", "seeds");
        Assert.True(Directory.Exists(root), $"The seed directory is missing: {root}");
        return root;
    }
}
