namespace AutoJMS;

public sealed class PrintSubmitResult
{
    public bool CompletedBySpooler { get; init; }
    public long ElapsedMs { get; init; }
    public string PrinterName { get; init; } = "";
    public string DocumentName { get; init; } = "";
    public int SpoolerJobsBefore { get; init; }
    public int SpoolerJobsAfter { get; init; }
    public string Reason { get; init; } = "";
}
