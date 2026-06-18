using System;
using System.Runtime.InteropServices;

namespace AutoJMS.Utils
{
    public static class PrinterDevModeHelper
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public short dmOrientation;
            public short dmPaperSize;
            public short dmPaperLength;
            public short dmPaperWidth;
            public short dmScale;
            public short dmCopies;
            public short dmDefaultSource;
            public short dmPrintQuality;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        }

        [DllImport("winspool.drv", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool ClosePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int DocumentProperties(IntPtr hwnd, IntPtr hPrinter, string pDeviceName, IntPtr pDevModeOutput, IntPtr pDevModeInput, int fMode);

        [DllImport("winspool.drv", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SetPrinter(IntPtr hPrinter, int level, IntPtr pPrinter, int command);

        [StructLayout(LayoutKind.Sequential)]
        private struct PRINTER_DEFAULTS
        {
            public IntPtr pDatatype;
            public IntPtr pDevMode;
            public int DesiredAccess;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PRINTER_INFO_9
        {
            public IntPtr pDevMode;
        }

        private const int DM_OUT_BUFFER = 2;
        private const int DM_IN_BUFFER = 8;
        private const int PRINTER_ACCESS_ADMINISTER = 0x00000004;
        private const int PRINTER_ACCESS_USE = 0x00000008;
        private const int STANDARD_RIGHTS_REQUIRED = 0x000F0000;
        private const int PRINTER_ALL_ACCESS = (STANDARD_RIGHTS_REQUIRED | PRINTER_ACCESS_ADMINISTER | PRINTER_ACCESS_USE);
        
        private const int DM_PAPERSIZE = 0x00000002;
        private const int DM_PAPERLENGTH = 0x00000004;
        private const int DM_PAPERWIDTH = 0x00000008;

        /// <summary>
        /// Sets the global DEVMODE paper size for the specified printer.
        /// widthHundredthsInch and heightHundredthsInch are in hundredths of an inch.
        /// Example: 3 inches = 300.
        /// Example: 4x6 inches = 400 x 600.
        /// </summary>
        public static bool SetGlobalPaperSize(string printerName, int widthHundredthsInch, int heightHundredthsInch)
        {
            IntPtr hPrinter = IntPtr.Zero;
            IntPtr pPd = IntPtr.Zero;
            IntPtr pDevMode = IntPtr.Zero;
            IntPtr pPi9 = IntPtr.Zero;

            try
            {
                int rawKind = 256; // Fallback to Custom
                try 
                {
                    var ps = new System.Drawing.Printing.PrinterSettings { PrinterName = printerName };
                    foreach (System.Drawing.Printing.PaperSize pSize in ps.PaperSizes)
                    {
                        if (Math.Abs(pSize.Width - widthHundredthsInch) < 5 && Math.Abs(pSize.Height - heightHundredthsInch) < 5)
                        {
                            rawKind = pSize.RawKind;
                            break;
                        }
                    }
                } 
                catch (Exception ex) 
                {
                    AppLogger.Warning($"[PrinterDevMode] Could not lookup PaperSizes: {ex.Message}");
                }

                PRINTER_DEFAULTS pd = new PRINTER_DEFAULTS();
                pd.DesiredAccess = PRINTER_ALL_ACCESS;

                pPd = Marshal.AllocHGlobal(Marshal.SizeOf(pd));
                Marshal.StructureToPtr(pd, pPd, false);

                if (!OpenPrinter(printerName, out hPrinter, pPd))
                {
                    AppLogger.Warning($"[PrinterDevMode] Failed to open printer '{printerName}'. Error: {Marshal.GetLastWin32Error()}");
                    return false;
                }

                int size = DocumentProperties(IntPtr.Zero, hPrinter, printerName, IntPtr.Zero, IntPtr.Zero, 0);
                if (size < 0)
                {
                    AppLogger.Warning($"[PrinterDevMode] Failed to get DEVMODE size for '{printerName}'.");
                    return false;
                }

                pDevMode = Marshal.AllocHGlobal(size);
                int ret = DocumentProperties(IntPtr.Zero, hPrinter, printerName, pDevMode, IntPtr.Zero, DM_OUT_BUFFER);
                if (ret < 0)
                {
                    AppLogger.Warning($"[PrinterDevMode] Failed to get DEVMODE for '{printerName}'.");
                    return false;
                }

                DEVMODE devMode = (DEVMODE)Marshal.PtrToStructure(pDevMode, typeof(DEVMODE));

                devMode.dmPaperSize = (short)rawKind;
                if (rawKind == 256)
                {
                    // Convert hundredths of inch to 0.1 mm (1 inch = 25.4 mm = 254 tenth-mm)
                    devMode.dmPaperWidth = (short)(widthHundredthsInch * 2.54);
                    devMode.dmPaperLength = (short)(heightHundredthsInch * 2.54);
                    devMode.dmFields |= (DM_PAPERSIZE | DM_PAPERWIDTH | DM_PAPERLENGTH);
                }
                else
                {
                    devMode.dmFields |= DM_PAPERSIZE;
                }

                Marshal.StructureToPtr(devMode, pDevMode, true);

                // Merge changes
                ret = DocumentProperties(IntPtr.Zero, hPrinter, printerName, pDevMode, pDevMode, DM_IN_BUFFER | DM_OUT_BUFFER);
                if (ret < 0)
                {
                    AppLogger.Warning($"[PrinterDevMode] Failed to merge DEVMODE for '{printerName}'.");
                    return false;
                }

                // Set global DEVMODE using SetPrinter (level 9)
                PRINTER_INFO_9 pi9 = new PRINTER_INFO_9();
                pi9.pDevMode = pDevMode;
                pPi9 = Marshal.AllocHGlobal(Marshal.SizeOf(pi9));
                Marshal.StructureToPtr(pi9, pPi9, false);

                if (!SetPrinter(hPrinter, 9, pPi9, 0))
                {
                    AppLogger.Warning($"[PrinterDevMode] Failed to SetPrinter (Level 9) for '{printerName}'. Error: {Marshal.GetLastWin32Error()}");
                    return false;
                }

                AppLogger.Info($"[PrinterDevMode] Successfully set global DEVMODE for '{printerName}' to {widthHundredthsInch}x{heightHundredthsInch} (hundredths inch).");
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"[PrinterDevMode] Exception changing DEVMODE: {ex.Message}");
                return false;
            }
            finally
            {
                if (pPi9 != IntPtr.Zero) Marshal.FreeHGlobal(pPi9);
                if (pDevMode != IntPtr.Zero) Marshal.FreeHGlobal(pDevMode);
                if (pPd != IntPtr.Zero) Marshal.FreeHGlobal(pPd);
                if (hPrinter != IntPtr.Zero) ClosePrinter(hPrinter);
            }
        }
    }
}
