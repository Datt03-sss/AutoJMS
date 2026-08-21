using AutoJMS.DataHub.Api.Domain;

namespace AutoJMS.DataHub.Api.Tests.Domain;

public sealed class ScanTimeParserTests
{
    [Fact]
    public void Parses_a_naive_jms_time_as_vietnam_time_and_returns_utc()
    {
        var result = ScanTimeParser.Parse("2026-08-17 18:24:22");

        Assert.True(result.Success);
        Assert.Equal(new DateTimeOffset(2026, 8, 17, 11, 24, 22, TimeSpan.Zero), result.UtcValue);
    }

    [Fact]
    public void Honors_an_explicit_offset_without_adding_vietnam_offset_again()
    {
        var result = ScanTimeParser.Parse("2026-08-17T18:24:22+02:00");

        Assert.True(result.Success);
        Assert.Equal(new DateTimeOffset(2026, 8, 17, 16, 24, 22, TimeSpan.Zero), result.UtcValue);
    }

    [Fact]
    public void Honors_zulu_time()
    {
        var result = ScanTimeParser.Parse("2026-08-17T18:24:22Z");

        Assert.True(result.Success);
        Assert.Equal(new DateTimeOffset(2026, 8, 17, 18, 24, 22, TimeSpan.Zero), result.UtcValue);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-time")]
    [InlineData("2026-08-17T18:24:22")]
    [InlineData("2026-08-17 18:24:61")]
    public void Rejects_missing_invalid_or_offsetless_iso_values_without_fallback(string? value)
    {
        var result = ScanTimeParser.Parse(value);

        Assert.False(result.Success);
        Assert.Null(result.UtcValue);
        Assert.Equal(ScanTimeParser.InvalidScanTimeCode, result.ErrorCode);
    }
}
