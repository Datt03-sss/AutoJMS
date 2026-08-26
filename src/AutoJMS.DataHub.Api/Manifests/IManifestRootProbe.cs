namespace AutoJMS.DataHub.Api.Manifests;

/// <summary>What an inspection of the control-plane root found.</summary>
public enum ManifestRootState
{
    /// <summary>The root exists and a probe file could be created in it.</summary>
    Writable,

    /// <summary>The root exists but could not be written to. Reads keep working; publishing does not.</summary>
    ReadOnly,

    /// <summary>The root does not exist or cannot be resolved at all.</summary>
    Missing
}

public readonly record struct ManifestRootProbeResult(ManifestRootState State, string? Detail);

/// <summary>
/// Answers whether the directory <see cref="ManifestStore"/> publishes into is actually
/// there and actually writable. Separated from the health check for the same reason
/// <see cref="Infrastructure.IDataHubDatabaseProbe"/> is: the check's decision table is
/// worth testing without a real filesystem underneath it.
/// </summary>
public interface IManifestRootProbe
{
    ManifestRootProbeResult Probe(string root);
}

/// <summary>
/// Probes the real filesystem by writing to it, because permissions on a bind mount are
/// only knowable by trying.
///
/// Deliberately does NOT create the root. <c>Directory.CreateDirectory</c> would turn a
/// mistyped DATAHUB_MANIFEST_ROOT into a container-local directory that looks healthy,
/// serves nothing, and disappears on the next redeploy — the exact fault this probe
/// exists to catch. In Compose the root is the <c>manifests_data</c> volume mounted at
/// <c>/manifests</c>, so on a correct deployment it is always already present.
/// </summary>
public sealed class FileSystemManifestRootProbe : IManifestRootProbe
{
    public ManifestRootProbeResult Probe(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
            return new ManifestRootProbeResult(ManifestRootState.Missing, "no path configured");

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(root);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new ManifestRootProbeResult(ManifestRootState.Missing, exception.GetType().Name);
        }

        if (!Directory.Exists(fullPath))
            return new ManifestRootProbeResult(ManifestRootState.Missing, "directory does not exist");

        // The probe file sits at the root and starts with a dot, so no request can ever be
        // routed onto it: ManifestObjectPath requires an allowlisted container prefix and
        // at least two segments. DeleteOnClose removes it even if the write throws.
        var probePath = Path.Combine(fullPath, $".healthcheck-{Environment.ProcessId}.tmp");
        try
        {
            using var stream = new FileStream(
                probePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose);
            stream.WriteByte(0);
            return new ManifestRootProbeResult(ManifestRootState.Writable, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new ManifestRootProbeResult(ManifestRootState.ReadOnly, exception.GetType().Name);
        }
    }
}
