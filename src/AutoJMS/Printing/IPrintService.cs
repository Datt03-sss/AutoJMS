using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS;

public interface IPrintService
{
    PrintMode CurrentMode { get; }
    event Action<int, int> OnPrintStatsChanged;
    event Action<PrintSafetyResult> OnPrintSafetyBlocked;
    event Action OnPrintSelectionCleared;
    Task SearchAndLoadAsync(string waybillsText, PrintMode mode);
    Task<bool> ValidateSelectedBeforePrintAsync(IEnumerable<string> waybills, string currentInputText);
    Task<IReadOnlyList<PrintApprovalInfo>> RefreshPrintApprovalInfoAsync(IEnumerable<string> waybills, int printType, string phase);
    Task<IReadOnlyList<PrintStatusSnapshot>> RefreshPrintStatusAsync(IEnumerable<string> waybills, int printType, PrintStatusRefreshReason reason, CancellationToken cancellationToken = default);
    void QueuePostPrintRefresh(IEnumerable<string> waybills, int printType);
    IReadOnlyList<PrintStatusSnapshot> GetLastPrintStatusSnapshots();
    PrintSafetyResult GetLastAllowedPrintSafetyResult(string waybillNo);
    void SelectAll(bool isChecked);
    void ClearSelection();
    List<string> GetSelectedWaybills();
    /// <summary>Rows behind the current grid — lets "In lại đơn" prefill its editor.</summary>
    IReadOnlyList<TrackingRow> GetLoadedPrintRows();
    void SetMode(PrintMode mode);
    void Reset();
}
