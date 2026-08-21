namespace AutoJMS.DataHub.Api.Domain;

public static class ChangeCursorWindow
{
    public static bool RequiresResync(long after, long prunedThrough, long current)
        => after < prunedThrough || after > current;
}
