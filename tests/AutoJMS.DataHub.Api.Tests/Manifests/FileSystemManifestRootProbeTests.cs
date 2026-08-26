using AutoJMS.DataHub.Api.Manifests;

namespace AutoJMS.DataHub.Api.Tests.Manifests;

public sealed class FileSystemManifestRootProbeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "autojms-manifest-probe-" + Guid.NewGuid().ToString("N"));
    private readonly FileSystemManifestRootProbe _probe = new();

    public FileSystemManifestRootProbeTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void An_existing_writable_directory_probes_as_writable()
    {
        Assert.Equal(ManifestRootState.Writable, _probe.Probe(_root).State);
    }

    [Fact]
    public void The_probe_leaves_nothing_behind()
    {
        _probe.Probe(_root);

        // The control-plane root is served over HTTP. A probe file that outlived the check
        // would be an artefact of the health check sitting in a published directory.
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }

    [Fact]
    public void A_directory_that_does_not_exist_probes_as_missing()
    {
        // Not created on demand: creating it would turn a mistyped DATAHUB_MANIFEST_ROOT
        // into a healthy-looking directory that serves nothing.
        var result = _probe.Probe(Path.Combine(_root, "not-mounted"));

        Assert.Equal(ManifestRootState.Missing, result.State);
        Assert.False(Directory.Exists(Path.Combine(_root, "not-mounted")));
    }

    [Fact]
    public void An_unconfigured_root_probes_as_missing()
    {
        Assert.Equal(ManifestRootState.Missing, _probe.Probe("").State);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
