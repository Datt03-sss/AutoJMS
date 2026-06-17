using System;

namespace AutoJMS;

public sealed class PrintJobCacheEntry
{
    public string CacheKey { get; init; } = "";
    public string WaybillNo { get; init; } = "";
    public byte[] PdfBytes { get; init; } = Array.Empty<byte>();
    public string LocalPdfPath { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public DateTime ExpiresAt { get; init; }
    public string PdfHash { get; init; } = "";

    public bool IsExpired => DateTime.Now > ExpiresAt;
}
