using System.Collections.Generic;

namespace AutoJMS;

public sealed class PrintJobRequest
{
    public List<string> Waybills { get; init; } = new();
    public string CurrentInputText { get; init; } = "";
    public int PrintType { get; init; }
    public int ApplyTypeCode { get; init; }
    public string ApiUrl { get; init; } = "";
}
