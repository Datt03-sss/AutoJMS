using AutoJMS.DataHub.Api.Domain;

namespace AutoJMS.DataHub.Api.Tests.Domain;

public sealed class ChangeCursorWindowTests
{
    [Theory]
    [InlineData(0, 0, 0, false)]
    [InlineData(0, 0, 1, false)]
    [InlineData(4, 5, 10, true)]
    [InlineData(5, 5, 10, false)]
    [InlineData(10, 5, 10, false)]
    [InlineData(11, 5, 10, true)]
    public void Requires_resync_only_outside_the_retained_cursor_window(
        long after,
        long prunedThrough,
        long current,
        bool expected)
    {
        Assert.Equal(expected, ChangeCursorWindow.RequiresResync(after, prunedThrough, current));
    }
}
