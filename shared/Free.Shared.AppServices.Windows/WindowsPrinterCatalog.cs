using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Free.Shared.AppServices.Windows;

public enum WindowsPrinterCatalogStatus
{
    Available,
    NoPrinters,
    Unavailable,
    Failed,
}

public sealed record WindowsPrinterCatalogResult(
    WindowsPrinterCatalogStatus Status,
    IReadOnlyList<string> Printers,
    string? DefaultPrinter,
    int? NativeErrorCode = null,
    string? FailureReason = null)
{
    public bool IsAvailable => Status == WindowsPrinterCatalogStatus.Available;

    public static WindowsPrinterCatalogResult FromQueues(
        IEnumerable<string> printers,
        string? defaultPrinter)
    {
        ArgumentNullException.ThrowIfNull(printers);

        var normalizedDefault = Normalize(defaultPrinter);
        var names = printers
            .Select(Normalize)
            .Where(static name => name is not null)
            .Select(static name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return names.Length == 0 && normalizedDefault is null
            ? new WindowsPrinterCatalogResult(WindowsPrinterCatalogStatus.NoPrinters, [], null)
            : new WindowsPrinterCatalogResult(
                WindowsPrinterCatalogStatus.Available,
                names,
                normalizedDefault);
    }

    public static WindowsPrinterCatalogResult Unavailable(string reason) =>
        new(
            WindowsPrinterCatalogStatus.Unavailable,
            [],
            null,
            FailureReason: Normalize(reason) ?? "Windows printer discovery is unavailable.");

    public static WindowsPrinterCatalogResult Failed(string reason, int? nativeErrorCode = null) =>
        new(
            WindowsPrinterCatalogStatus.Failed,
            [],
            null,
            nativeErrorCode,
            Normalize(reason) ?? "Windows printer discovery failed.");

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public interface IWindowsPrinterCatalog
{
    WindowsPrinterCatalogResult Discover();
}

/// <summary>
/// App-neutral Windows printer queue discovery backed by winspool.
/// </summary>
public sealed class WindowsPrinterCatalog : IWindowsPrinterCatalog
{
    private const uint PrinterEnumLocal = 0x00000002;
    private const uint PrinterEnumConnections = 0x00000004;
    private const int ErrorInsufficientBuffer = 122;

    public WindowsPrinterCatalogResult Discover()
    {
        if (!OperatingSystem.IsWindows())
            return WindowsPrinterCatalogResult.Unavailable(
                "Windows printer discovery is available only on Windows.");

        try
        {
            return WindowsPrinterCatalogResult.FromQueues(
                EnumeratePrinters(),
                TryGetDefaultPrinter());
        }
        catch (Win32Exception ex)
        {
            return WindowsPrinterCatalogResult.Failed(
                $"Windows printer discovery failed: {ex.Message}",
                ex.NativeErrorCode);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return WindowsPrinterCatalogResult.Failed(
                $"Windows printer discovery failed: {ex.Message}");
        }
    }

    private static IReadOnlyList<string> EnumeratePrinters()
    {
        var flags = PrinterEnumLocal | PrinterEnumConnections;
        if (EnumPrinters(flags, null, 4, IntPtr.Zero, 0, out var bytesNeeded, out _) ||
            bytesNeeded == 0)
        {
            return [];
        }

        var error = Marshal.GetLastWin32Error();
        if (error != ErrorInsufficientBuffer)
            throw new Win32Exception(error);

        var buffer = Marshal.AllocHGlobal(checked((int)bytesNeeded));
        try
        {
            if (!EnumPrinters(flags, null, 4, buffer, bytesNeeded, out _, out var count))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var entrySize = Marshal.SizeOf<PrinterInfo4>();
            var printers = new List<string>((int)count);
            for (var index = 0; index < count; index++)
            {
                var info = Marshal.PtrToStructure<PrinterInfo4>(buffer + index * entrySize);
                var name = Marshal.PtrToStringUni(info.PrinterName);
                if (!string.IsNullOrWhiteSpace(name))
                    printers.Add(name);
            }

            return printers;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string? TryGetDefaultPrinter()
    {
        var length = 0;
        GetDefaultPrinter(null, ref length);
        if (length <= 1)
            return null;

        var buffer = new StringBuilder(length);
        return GetDefaultPrinter(buffer, ref length) && buffer.Length > 0
            ? buffer.ToString()
            : null;
    }

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetDefaultPrinter(StringBuilder? printerName, ref int bufferLength);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumPrinters(
        uint flags,
        string? name,
        uint level,
        IntPtr printerEnum,
        uint cbBuf,
        out uint pcbNeeded,
        out uint pcReturned);

    [StructLayout(LayoutKind.Sequential)]
    private struct PrinterInfo4
    {
        public IntPtr PrinterName;
        public IntPtr ServerName;
        public uint Attributes;
    }
}
