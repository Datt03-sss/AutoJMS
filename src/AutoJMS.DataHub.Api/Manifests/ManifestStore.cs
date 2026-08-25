using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using AutoJMS.DataHub.Api.Configuration;

namespace AutoJMS.DataHub.Api.Manifests;

/// <summary>A stored control-plane object plus the metadata a conditional GET needs.</summary>
public sealed record ManifestObject(string CanonicalPath, byte[] Content, string ETag, DateTimeOffset LastModified)
{
    public string ContentType => ManifestObjectPath.ContentTypeFor(CanonicalPath);
}

/// <summary>Outcome of a publish. <paramref name="Created"/> separates 201 from 204.</summary>
public sealed record ManifestWriteResult(string CanonicalPath, string ETag, int Length, bool Created);

/// <summary>
/// Filesystem-backed store for the manifest/config control plane.
///
/// Deliberately not Postgres: these objects are small, written only by the release
/// script, and read anonymously before a device has any identity — so a blob table
/// would buy nothing and would cost a schema migration (a protected area in this
/// repo). The trade-off is that the objects live on one node's volume; a
/// multi-node DataHub would have to replace this with shared storage, and loss of
/// the volume is recovered by re-publishing from release/build-release.ps1.
/// </summary>
public sealed class ManifestStore
{
    private readonly string _root;
    private readonly ILogger<ManifestStore> _logger;
    private readonly ConcurrentDictionary<string, CachedObject> _cache = new(StringComparer.Ordinal);

    /// <summary>Publish ceiling, matched to the Kestrel request body limit in Program.cs.</summary>
    public const int MaximumObjectBytes = 1024 * 1024;

    public ManifestStore(DataHubRuntimeOptions options, ILogger<ManifestStore> logger)
    {
        _root = Path.GetFullPath(options.ManifestRoot);
        _logger = logger;
    }

    /// <summary>The directory objects are served from. Exposed for diagnostics only.</summary>
    public string Root => _root;

    /// <summary>
    /// Reads an object, or null when it has never been published. The cache is keyed
    /// on the canonical path and only used when the file's size and write time still
    /// match, so an object replaced underneath the process is still served correctly.
    /// </summary>
    public async Task<ManifestObject?> ReadAsync(string canonicalPath, CancellationToken cancellationToken)
    {
        var file = new FileInfo(ResolveFullPath(canonicalPath));
        if (!file.Exists)
            return null;

        var lastWriteUtc = file.LastWriteTimeUtc;
        var length = file.Length;
        if (_cache.TryGetValue(canonicalPath, out var cached)
            && cached.LastWriteUtc == lastWriteUtc
            && cached.Content.LongLength == length)
        {
            return new ManifestObject(canonicalPath, cached.Content, cached.ETag, new DateTimeOffset(lastWriteUtc, TimeSpan.Zero));
        }

        byte[] content;
        try
        {
            content = await File.ReadAllBytesAsync(file.FullName, cancellationToken);
        }
        catch (IOException exception)
        {
            // A publish replaces the file with File.Move, so a read can briefly lose
            // the race. Treat it as "not published yet" rather than a 500: the client
            // retries on its next poll and its cached copy covers the gap.
            _logger.LogWarning(exception, "Manifest object {ObjectPath} could not be read.", canonicalPath);
            return null;
        }

        var etag = ComputeETag(content);
        _cache[canonicalPath] = new CachedObject(content, etag, lastWriteUtc);
        return new ManifestObject(canonicalPath, content, etag, new DateTimeOffset(lastWriteUtc, TimeSpan.Zero));
    }

    /// <summary>
    /// Publishes an object atomically: the payload is written to a sibling temp file
    /// and moved over the target, so a concurrent reader sees either the whole old
    /// object or the whole new one, never a half-written file.
    /// </summary>
    public async Task<ManifestWriteResult> WriteAsync(string canonicalPath, byte[] content, CancellationToken cancellationToken)
    {
        var fullPath = ResolveFullPath(canonicalPath);
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);

        var existed = File.Exists(fullPath);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, content, cancellationToken);
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }

        var etag = ComputeETag(content);
        _cache[canonicalPath] = new CachedObject(content, etag, File.GetLastWriteTimeUtc(fullPath));
        return new ManifestWriteResult(canonicalPath, etag, content.Length, !existed);
    }

    /// <summary>
    /// Validates a payload before it is allowed to replace a live object. A JSON
    /// object that does not parse would otherwise brick every station that reads
    /// it, and the store is the last place that can still refuse it.
    /// </summary>
    public static bool TryValidatePayload(string canonicalPath, byte[] content, out string failureReason)
    {
        failureReason = "";

        if (content.Length == 0)
        {
            failureReason = "The request body is empty.";
            return false;
        }

        if (content.Length > MaximumObjectBytes)
        {
            failureReason = $"The object exceeds {MaximumObjectBytes} bytes.";
            return false;
        }

        if (!ManifestObjectPath.RequiresJsonBody(canonicalPath))
            return true;

        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
            {
                failureReason = "A .json object must contain a JSON object or array.";
                return false;
            }
        }
        catch (JsonException exception)
        {
            failureReason = "The .json object is not valid JSON: " + exception.Message;
            return false;
        }

        return true;
    }

    /// <summary>Strong ETag over the content, so an unchanged object answers 304 without a body.</summary>
    private static string ComputeETag(byte[] content)
        => "\"" + Convert.ToHexStringLower(SHA256.HashData(content)) + "\"";

    private string ResolveFullPath(string canonicalPath)
    {
        // ManifestObjectPath has already rejected traversal, separators and reserved
        // characters, so this combine cannot escape the root. Re-checking containment
        // keeps that guarantee local instead of trusting a caller to have validated.
        var combined = Path.GetFullPath(Path.Combine(_root, canonicalPath.Replace('/', Path.DirectorySeparatorChar)));
        if (!combined.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException($"The object path '{canonicalPath}' resolves outside the manifest root.");

        return combined;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // A leftover temp file is inert; it is never served because its name is
            // not a valid object path.
        }
    }

    private sealed record CachedObject(byte[] Content, string ETag, DateTime LastWriteUtc);
}
