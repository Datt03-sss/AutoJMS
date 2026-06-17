using System;

namespace AutoJMS;

public sealed class CurrentPrintAttempt
{
    public string AttemptId { get; init; } = Guid.NewGuid().ToString("N");
    public string WaybillNo { get; init; } = "";
    public string PdfUrl { get; set; } = "";
    public byte[] PdfBytes { get; set; } = Array.Empty<byte>();
    public string TempPdfPath { get; set; } = "";
    public bool PrintWaybillRequested { get; private set; }
    public bool Printed { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.Now;

    public void MarkPrintWaybillRequested()
    {
        if (PrintWaybillRequested)
        {
            throw new InvalidOperationException(
                $"printWaybill already requested for attempt {AttemptId}");
        }

        PrintWaybillRequested = true;
    }
}
