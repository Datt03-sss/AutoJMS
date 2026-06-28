using Xunit;

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
}
