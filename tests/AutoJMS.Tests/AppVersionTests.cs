using Xunit;
using AutoJMS;

namespace AutoJMS.Tests;

public sealed class AppVersionTests
{
    [Theory]
    [InlineData("1.26.6+bb20dc4d3001d866cc96282071d62b1f13750d4f", "1.26.6")]
    [InlineData("1.26.8-beta.1+abcdef", "1.26.8-beta.1")]
    [InlineData("1.26.6", "1.26.6")]
    [InlineData(" 1.26.6+abcdef ", "1.26.6")]
    public void NormalizeDisplayVersion_RemovesGitBuildMetadata(string input, string expected)
    {
        Assert.Equal(expected, AppVersion.NormalizeDisplayVersion(input));
    }

    // AutoJMS tags every build `-Release`. Read as a SemVer prerelease label
    // it sorts BELOW the plain version, so the newest build looked like a
    // downgrade; and `beta.1-Release` / `beta.2-Release` both parsed their
    // build number as 0, so no beta ever superseded another.
    [Theory]
    [InlineData("1.26.11", "1.26.12-Release", true)]
    [InlineData("1.26.12", "1.26.12-Release", false)]
    [InlineData("1.26.12-Release", "1.26.12", false)]
    [InlineData("1.26.12-beta.1-Release", "1.26.12-beta.2-Release", true)]
    [InlineData("1.26.12-beta.2-Release", "1.26.12-beta.1-Release", false)]
    [InlineData("1.26.12-beta.1", "1.26.12-Release", true)]
    [InlineData("1.26.11", "v1.26.12-Release+abc1234", true)]
    public void IsUpgrade_HandlesReleaseSuffixedTags(string current, string target, bool expected)
    {
        Assert.Equal(expected, UpdateChannelDialog.IsUpgrade(current, target));
    }

    [Theory]
    [InlineData("1.26.12", "1.26.11-Release", true)]
    [InlineData("1.26.12", "1.26.12-Release", false)]
    public void IsDowngrade_HandlesReleaseSuffixedTags(string current, string target, bool expected)
    {
        Assert.Equal(expected, UpdateChannelDialog.IsDowngrade(current, target));
    }
}
