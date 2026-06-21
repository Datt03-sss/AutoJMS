using System.Threading.Tasks;

namespace AutoJMS;

public interface IPrinterSpoolerSubmitter
{
    Task<PrintSubmitResult> SubmitPrintAsync(PrintJobCacheEntry job, string firstWaybill);
}
