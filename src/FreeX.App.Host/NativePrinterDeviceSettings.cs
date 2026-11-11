using System.ComponentModel;
using System.Drawing.Printing;
using System.Printing;
using System.Printing.Interop;
using System.Runtime.InteropServices;

namespace FreeX.App.Host;

/// <summary>
/// Owns the driver-level device settings (DEVMODE) the user picks through a printer's own
/// properties sheet, so "Printer Properties" behaves like Excel's: it opens the driver sheet for the
/// selected printer and the choices made there (tray, colour, quality, ...) apply to the next print
/// in this session instead of being discarded.
/// </summary>
internal static class NativePrinterDeviceSettings
{
    private const int DmOutBuffer = 2;
    private const int DmInPrompt = 4;
    private const int DmInBuffer = 8;
    private const int IdOk = 1;

    // DEVMODEW: dmDeviceName is 32 WCHARs (64 bytes), then dmSpecVersion and dmDriverVersion (2 bytes
    // each), which puts dmSize at offset 68 and dmDriverExtra at offset 70.
    private const int DmSizeOffset = 68;
    private const int DmDriverExtraOffset = 70;

    private static readonly Dictionary<string, byte[]> SessionDevModes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns the requested printer, or the Windows default printer when none is named.</summary>
    public static string? ResolvePrinterName(string? requestedPrinterName)
    {
        if (!string.IsNullOrWhiteSpace(requestedPrinterName))
            return requestedPrinterName.Trim();

        var defaults = new PrinterSettings();
        return defaults.IsValid && !string.IsNullOrWhiteSpace(defaults.PrinterName)
            ? defaults.PrinterName
            : null;
    }

    /// <summary>
    /// Shows the driver's document-properties sheet for <paramref name="printerName"/>. Returns
    /// <see langword="true"/> when the user accepted it; the accepted settings are then remembered for
    /// the rest of the session.
    /// </summary>
    public static bool ShowPropertySheet(string printerName, IntPtr ownerHandle)
    {
        var settings = new PrinterSettings { PrinterName = printerName };
        if (!settings.IsValid)
            throw new InvalidPrinterException(settings);

        ApplySessionDevMode(settings);
        var devModeHandle = settings.GetHdevmode();
        try
        {
            var devMode = GlobalLock(devModeHandle);
            if (devMode == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            try
            {
                if (!OpenPrinter(printerName, out var printerHandle, IntPtr.Zero))
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                try
                {
                    var result = DocumentProperties(
                        ownerHandle,
                        printerHandle,
                        printerName,
                        devMode,
                        devMode,
                        DmInBuffer | DmOutBuffer | DmInPrompt);
                    if (result < 0)
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    if (result != IdOk)
                        return false;
                }
                finally
                {
                    ClosePrinter(printerHandle);
                }

                SessionDevModes[printerName] = CopyDevMode(devMode);
                return true;
            }
            finally
            {
                GlobalUnlock(devModeHandle);
            }
        }
        finally
        {
            GlobalFree(devModeHandle);
        }
    }

    /// <summary>Applies the settings remembered for this printer, if any, to <paramref name="settings"/>.</summary>
    public static void ApplySessionDevMode(PrinterSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.PrinterName) ||
            !SessionDevModes.TryGetValue(settings.PrinterName, out var devModeBytes))
        {
            return;
        }

        var buffer = Marshal.AllocHGlobal(devModeBytes.Length);
        try
        {
            Marshal.Copy(devModeBytes, 0, buffer, devModeBytes.Length);
            settings.SetHdevmode(buffer);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A remembered DEVMODE the driver no longer accepts must not block printing.
            SessionDevModes.Remove(settings.PrinterName);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// Converts the device settings carried by <paramref name="settings"/> (including anything chosen
    /// in the native print dialog's Preferences) into a WPF print ticket, or <see langword="null"/>
    /// when the driver cannot convert them.
    /// </summary>
    public static PrintTicket? TryCreatePrintTicket(PrinterSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.PrinterName))
            return null;

        try
        {
            var devModeHandle = settings.GetHdevmode();
            byte[] devModeBytes;
            try
            {
                var devMode = GlobalLock(devModeHandle);
                if (devMode == IntPtr.Zero)
                    return null;

                try
                {
                    devModeBytes = CopyDevMode(devMode);
                }
                finally
                {
                    GlobalUnlock(devModeHandle);
                }
            }
            finally
            {
                GlobalFree(devModeHandle);
            }

            using var converter = new PrintTicketConverter(
                settings.PrinterName,
                PrintTicketConverter.MaxPrintSchemaVersion);
            return converter.ConvertDevModeToPrintTicket(devModeBytes);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>
    /// Makes the print ticket's paper size and orientation follow the worksheet's page, so a
    /// landscape or non-default-size sheet is not printed on the printer's default portrait paper.
    /// </summary>
    public static void ApplyPageGeometry(PrintTicket ticket, System.Windows.Size pageSize)
    {
        if (pageSize.Width <= 0 || pageSize.Height <= 0 ||
            double.IsNaN(pageSize.Width) || double.IsNaN(pageSize.Height) ||
            double.IsInfinity(pageSize.Width) || double.IsInfinity(pageSize.Height))
        {
            return;
        }

        var shortEdge = Math.Min(pageSize.Width, pageSize.Height);
        var longEdge = Math.Max(pageSize.Width, pageSize.Height);
        ticket.PageMediaSize = new PageMediaSize(shortEdge, longEdge);
        ticket.PageOrientation = pageSize.Width > pageSize.Height
            ? PageOrientation.Landscape
            : PageOrientation.Portrait;
    }

    private static byte[] CopyDevMode(IntPtr devMode)
    {
        var size = (ushort)Marshal.ReadInt16(devMode, DmSizeOffset);
        var driverExtra = (ushort)Marshal.ReadInt16(devMode, DmDriverExtraOffset);
        var bytes = new byte[size + driverExtra];
        Marshal.Copy(devMode, bytes, 0, bytes.Length);
        return bytes;
    }

    [DllImport("winspool.drv", EntryPoint = "DocumentPropertiesW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int DocumentProperties(
        IntPtr hWnd,
        IntPtr hPrinter,
        string pDeviceName,
        IntPtr pDevModeOutput,
        IntPtr pDevModeInput,
        int fMode);

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

    [DllImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);
}
