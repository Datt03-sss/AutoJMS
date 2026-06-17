#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum GoogleSheetsAccessMode
    {
        Auto,
        TokenBroker,
        ServerOnly,
        LegacyLocalOnly,
        Disabled
    }

    public enum GoogleSheetWriteOperation
    {
        Clear,
        Update
    }

    public sealed class GoogleSheetReadRequest
    {
        public string SpreadsheetId { get; init; } = "";
        public IReadOnlyList<string> Ranges { get; init; } = Array.Empty<string>();
    }

    public sealed class GoogleSheetReadResult
    {
        public bool Success { get; init; }
        public string ProviderName { get; init; } = "";
        public string Message { get; init; } = "";
        public List<IList<IList<object>>> ValueRanges { get; init; } = new();

        public static GoogleSheetReadResult Fail(string providerName, string message) =>
            new() { Success = false, ProviderName = providerName, Message = message };
    }

    public sealed class GoogleSheetWriteRequest
    {
        public GoogleSheetWriteOperation Operation { get; init; }
        public string SpreadsheetId { get; init; } = "";
        public string Range { get; init; } = "";
        public IList<IList<object>> Values { get; init; } = new List<IList<object>>();
    }

    public sealed class GoogleSheetWriteResult
    {
        public bool Success { get; init; }
        public string ProviderName { get; init; } = "";
        public string Message { get; init; } = "";

        public static GoogleSheetWriteResult Ok(string providerName) =>
            new() { Success = true, ProviderName = providerName };

        public static GoogleSheetWriteResult Fail(string providerName, string message) =>
            new() { Success = false, ProviderName = providerName, Message = message };
    }

    public interface IGoogleSheetsProvider
    {
        string ProviderName { get; }

        Task<bool> IsAvailableAsync(CancellationToken cancellationToken);

        Task<GoogleSheetReadResult> ReadAsync(
            GoogleSheetReadRequest request,
            CancellationToken cancellationToken);

        Task<GoogleSheetWriteResult> WriteAsync(
            GoogleSheetWriteRequest request,
            CancellationToken cancellationToken);
    }
}
