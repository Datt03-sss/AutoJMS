using System.Diagnostics.CodeAnalysis;

namespace AutoJMS.DataHub.Api.Manifests;

/// <summary>
/// Validates and canonicalises a control-plane object path such as
/// <c>manifest/version-latest.json</c>.
///
/// These objects are fetched anonymously by every desktop station, so this
/// allowlist is the only thing between a public GET and whatever else happens to
/// sit on the API host's filesystem. It is deliberately stricter than
/// <see cref="Path.GetFullPath(string)"/> containment checks: rejecting the input
/// outright is easier to reason about than normalising a hostile path and hoping
/// the result still lands inside the root.
/// </summary>
public static class ManifestObjectPath
{
    /// <summary>
    /// The only top-level containers that may be published or served. Derived from
    /// what the desktop and the release script already request:
    /// <c>manifest/</c> and <c>configs/</c> (VpsManifestService, VpsRuntimePolicyService),
    /// <c>selector-updates/</c> (SmallUpdateService, including its encrypted payload and
    /// detached signature) and <c>modules/</c> (VpsModuleProvider).
    /// </summary>
    public static readonly string[] AllowedPrefixes = ["manifest", "configs", "selector-updates", "modules"];

    /// <summary>Whole-path budget. Long enough for a versioned payload, short enough to log.</summary>
    public const int MaximumLength = 200;

    private const int MaximumSegments = 4;
    private const int MaximumSegmentLength = 80;

    /// <summary>
    /// Canonicalises <paramref name="candidate"/> or explains why it was rejected.
    /// The canonical form has no leading slash and is safe to append to a root
    /// directory: every segment is a plain name, so no segment can escape upwards.
    /// </summary>
    public static bool TryCanonicalize(
        string? candidate,
        [NotNullWhen(true)] out string? canonical,
        out string failureReason)
    {
        canonical = null;
        failureReason = "";

        if (string.IsNullOrWhiteSpace(candidate))
        {
            failureReason = "The object path is required.";
            return false;
        }

        var trimmed = candidate.Trim().TrimStart('/');
        if (trimmed.Length == 0)
        {
            failureReason = "The object path is required.";
            return false;
        }

        if (trimmed.Length > MaximumLength)
        {
            failureReason = $"The object path exceeds {MaximumLength} characters.";
            return false;
        }

        // Reject the whole class of traversal and scheme tricks before splitting, so
        // no later step has to be clever about them.
        if (trimmed.Contains('\\', StringComparison.Ordinal)
            || trimmed.Contains("..", StringComparison.Ordinal)
            || trimmed.Contains("//", StringComparison.Ordinal)
            || trimmed.Contains(':', StringComparison.Ordinal)
            || trimmed.EndsWith('/'))
        {
            failureReason = "The object path contains a path traversal or a reserved character.";
            return false;
        }

        var segments = trimmed.Split('/');

        // The container is checked BEFORE the segment count so that the reason a path
        // was rejected is the interesting one. Checking depth first meant a bare
        // "appsettings.json" was reported as having too few segments, which reads like
        // "add another segment and it will work" — the opposite of the truth.
        //
        // Case-sensitive: the objects live on a case-sensitive filesystem, and the
        // desktop always requests the lowercase form. Accepting "Manifest/" here
        // would create a second, silently empty namespace on Linux.
        if (Array.IndexOf(AllowedPrefixes, segments[0]) < 0)
        {
            failureReason = $"'{segments[0]}' is not a published container. Allowed: {string.Join(", ", AllowedPrefixes)}.";
            return false;
        }

        if (segments.Length is < 2 or > MaximumSegments)
        {
            failureReason = $"The object path must contain between 2 and {MaximumSegments} segments.";
            return false;
        }

        foreach (var segment in segments)
        {
            if (!IsSafeSegment(segment))
            {
                failureReason = "Every path segment must be 1-80 characters of letters, digits, '.', '_' or '-', and may not start or end with a separator character.";
                return false;
            }
        }

        canonical = string.Join('/', segments);
        return true;
    }

    /// <summary>True when this object must parse as JSON before it may be published.</summary>
    public static bool RequiresJsonBody(string canonicalPath)
        => canonicalPath.EndsWith(".json", StringComparison.Ordinal);

    /// <summary>
    /// Content type served for an object. Only JSON is given a specific type; every
    /// other payload is an opaque blob (an encrypted small-update file, a detached
    /// signature) and must never be served as something a browser would render.
    /// </summary>
    public static string ContentTypeFor(string canonicalPath)
        => RequiresJsonBody(canonicalPath) ? "application/json; charset=utf-8" : "application/octet-stream";

    private static bool IsSafeSegment(string segment)
    {
        if (segment.Length is 0 or > MaximumSegmentLength)
            return false;

        if (!IsAlphanumeric(segment[0]) || !IsAlphanumeric(segment[^1]))
            return false;

        foreach (var character in segment)
        {
            if (!IsAlphanumeric(character) && character is not ('.' or '_' or '-'))
                return false;
        }

        // Each dot-delimited part must stand on its own. Testing only the first and
        // last character of the whole segment let "trailing-.json" through: it ends
        // with 'n', so the dangling hyphen in the stem was invisible. Every legitimate
        // object ("runtime-policy.ultra.json", "runtime-config.enc.sig") passes this.
        foreach (var part in segment.Split('.'))
        {
            if (part.Length == 0 || part[0] == '-' || part[^1] == '-')
                return false;
        }

        return true;
    }

    private static bool IsAlphanumeric(char character)
        => character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9';
}
