using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using FreeP.App.Compositor;
using FreeP.App.Recording;

namespace FreeP.App.Recording.Windows;

/// <summary>
/// Windows-native printer discovery and PDF handoff for the Avalonia host.
/// The shell owns PDF rendering and spooling, so this adapter does not introduce a second
/// presentation renderer or require WPF in the cross-platform application.
/// </summary>
public static class WindowsNativePrintOutput
{
    public static LinuxNativeOutputCapabilities Detect()
    {
        if (!OperatingSystem.IsWindows())
            return LinuxNativeOutputCapabilities.Unavailable(
                "Windows native output is available only on Windows.");

        var print = DetectPrint();
        return new LinuxNativeOutputCapabilities(
            print,
            DetectWindowsVideoCapability(new WindowsNativeRecordingDeviceCatalog()));
    }

    /// <summary>
    /// Builds the Windows MediaComposition capability from the devices that the host can
    /// actually enumerate. Keeping this injectable makes the advertised capture surface
    /// testable without requiring a microphone or camera on the test machine.
    /// </summary>
    public static LinuxVideoEncoderCapability DetectWindowsVideoCapability(
        IWindowsRecordingDeviceCatalog deviceCatalog)
    {
        ArgumentNullException.ThrowIfNull(deviceCatalog);

        try
        {
            var devices = deviceCatalog.EnumerateDevices();
            var hasMicrophone = devices.Any(device =>
                device.Kind == SlideShowRecordingCaptureDeviceKind.Microphone &&
                device.IsAvailable);
            var hasCamera = devices.Any(device =>
                device.Kind == SlideShowRecordingCaptureDeviceKind.Camera &&
                device.IsAvailable);

            return new LinuxVideoEncoderCapability(
                CanEncodeMp4: true,
                ExecutablePath: WindowsNativeVideoExportAdapter.ExecutablePath,
                EncoderName: "Windows MediaComposition",
                CanCaptureNarration: hasMicrophone,
                Reason: BuildWindowsVideoCapabilityReason(hasMicrophone, hasCamera),
                CanCaptureCameraAndMedia: hasCamera);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new LinuxVideoEncoderCapability(
                CanEncodeMp4: true,
                ExecutablePath: WindowsNativeVideoExportAdapter.ExecutablePath,
                EncoderName: "Windows MediaComposition",
                CanCaptureNarration: false,
                Reason: $"Windows MediaComposition video export is available, but recording device detection failed: {ex.Message}",
                CanCaptureCameraAndMedia: false);
        }
    }

    private static string BuildWindowsVideoCapabilityReason(bool hasMicrophone, bool hasCamera) =>
        (hasMicrophone, hasCamera) switch
        {
            (true, true) => "Windows MediaComposition video export, delayed multi-track narration, and captured camera PIP are available.",
            (true, false) => "Windows MediaComposition video export and narration capture are available; no camera device is currently available for camera PIP.",
            (false, true) => "Windows MediaComposition video export and camera PIP are available; no microphone device is currently available for narration.",
            _ => "Windows MediaComposition video export is available; no microphone device is currently available for narration, and no camera device is currently available for camera PIP."
        };

    public static LinuxNativePrintCapability DetectPrint()
    {
        if (!OperatingSystem.IsWindows())
            return LinuxNativePrintCapability.Unavailable(
                "Windows native printing is available only on Windows.");

        var printer = TryGetDefaultPrinter();
        return string.IsNullOrWhiteSpace(printer)
            ? LinuxNativePrintCapability.Unavailable(
                "Windows reported no default printer queue.")
            : new LinuxNativePrintCapability(
                CanPrint: true,
                ExecutablePath: "windows-shell-print",
                PrinterName: printer,
                Reason: $"Windows printer queue '{printer}' is available through the native print handoff.");
    }

    public static LinuxNativePrintCapability ForPrinter(string printerName)
    {
        if (!OperatingSystem.IsWindows())
            return LinuxNativePrintCapability.Unavailable(
                "Windows native printing is available only on Windows.");

        var normalized = printerName?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
            return LinuxNativePrintCapability.Unavailable("Select a Windows printer queue first.");

        var knownPrinters = GetPrinters();
        return knownPrinters.Any(printer =>
                string.Equals(printer, normalized, StringComparison.OrdinalIgnoreCase))
            ? new LinuxNativePrintCapability(
                CanPrint: true,
                ExecutablePath: "windows-shell-print",
                PrinterName: normalized,
                Reason: $"Windows printer queue '{normalized}' is available through the native print handoff.")
            : LinuxNativePrintCapability.Unavailable(
                $"Windows printer queue '{normalized}' is no longer available.");
    }

    public static IReadOnlyList<string> GetPrinters()
    {
        if (!OperatingSystem.IsWindows())
            return [];

        const uint printerEnumLocal = 0x00000002;
        const uint printerEnumConnections = 0x00000004;
        if (EnumPrinters(
                printerEnumLocal | printerEnumConnections,
                null,
                level: 4,
                IntPtr.Zero,
                0,
                out var bytesNeeded,
                out _)
            || bytesNeeded == 0)
        {
            return [];
        }

        var buffer = Marshal.AllocHGlobal(checked((int)bytesNeeded));
        try
        {
            if (!EnumPrinters(
                    printerEnumLocal | printerEnumConnections,
                    null,
                    level: 4,
                    buffer,
                    bytesNeeded,
                    out _,
                    out var count))
            {
                return [];
            }

            var size = Marshal.SizeOf<PrinterInfo4>();
            var printers = new List<string>((int)count);
            for (var index = 0; index < count; index++)
            {
                var info = Marshal.PtrToStructure<PrinterInfo4>(buffer + index * size);
                var name = Marshal.PtrToStringUni(info.PrinterName);
                if (!string.IsNullOrWhiteSpace(name))
                    printers.Add(name.Trim());
            }

            return printers
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// Opens the native Windows printer-selection dialog and returns the queue selected by the
    /// user. The Avalonia host owns the surrounding print plan; this method only supplies the
    /// OS-owned queue selection surface that PowerPoint exposes from its Print workflow.
    /// </summary>
    public static bool TryShowPrinterSelectionDialog(
        string? currentPrinter,
        out string? selectedPrinter)
    {
        selectedPrinter = null;
        if (!OperatingSystem.IsWindows())
            return false;

        var dialog = default(PrintDlgExStruct);
        try
        {
            // PrintDlgEx is the Windows common-dialog surface used by native print
            // workflows. Calling it directly keeps this adapter free of a Windows
            // Forms runtime dependency, which matters for FreeP's non-Windows RIDs.
            _ = currentPrinter;
            dialog = new PrintDlgExStruct
            {
                StructSize = (uint)Marshal.SizeOf<PrintDlgExStruct>(),
                Flags = PrintDialogFlags.NoPageNums | PrintDialogFlags.NoSelection,
            };

            var result = PrintDlgEx(ref dialog);
            if (result != 0 || dialog.ResultAction != PrintDialogResultAction.Print)
                return false;

            selectedPrinter = ReadSelectedPrinter(dialog.DevNames);
            return !string.IsNullOrWhiteSpace(selectedPrinter);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A missing shell/desktop printer provider should not take down the portable
            // print pane; the existing queue picker and submission path remain available.
            return false;
        }
        finally
        {
            // PrintDlgEx transfers these global handles to the caller.
            if (dialog.DevMode != IntPtr.Zero)
                GlobalFree(dialog.DevMode);
            if (dialog.DevNames != IntPtr.Zero)
                GlobalFree(dialog.DevNames);
        }
    }

    private static string? ReadSelectedPrinter(IntPtr hDevNames)
    {
        if (hDevNames == IntPtr.Zero)
            return null;

        var locked = GlobalLock(hDevNames);
        if (locked == IntPtr.Zero)
            return null;

        try
        {
            var names = Marshal.PtrToStructure<DevNames>(locked);
            return Marshal.PtrToStringUni(
                IntPtr.Add(locked, checked(names.DeviceOffset * sizeof(char))))?.Trim();
        }
        finally
        {
            GlobalUnlock(hDevNames);
        }
    }

    public static ILinuxNativePrintHandoffAdapter CreateAdapter(
        LinuxNativePrintCapability capability) =>
        new WindowsNativePrintHandoffAdapter(capability);

    public static ILinuxVideoExportAdapter CreateVideoAdapter(
        LinuxVideoEncoderCapability capability) =>
        string.Equals(capability.ExecutablePath, WindowsNativeVideoExportAdapter.ExecutablePath, StringComparison.Ordinal)
            ? new WindowsNativeVideoExportAdapter(capability)
            : new LinuxVideoExportAdapter(capability);

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

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode)]
    private static extern int PrintDlgEx(ref PrintDlgExStruct dialog);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr handle);

    [Flags]
    private enum PrintDialogFlags : uint
    {
        NoSelection = 0x00000004,
        NoPageNums = 0x00000008,
    }

    private enum PrintDialogResultAction : uint
    {
        None = 0,
        Print = 1,
        Apply = 2,
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PrintDlgExStruct
    {
        public uint StructSize;
        public IntPtr Owner;
        public IntPtr DevMode;
        public IntPtr DevNames;
        public IntPtr DeviceContext;
        public PrintDialogFlags Flags;
        public uint Flags2;
        public uint ExclusionFlags;
        public uint PageRangeCount;
        public uint MaxPageRangeCount;
        public IntPtr PageRanges;
        public uint MinPage;
        public uint MaxPage;
        public uint Copies;
        public IntPtr Instance;
        public IntPtr PrintTemplateName;
        public IntPtr Callback;
        public uint PropertyPageCount;
        public IntPtr PropertyPages;
        public uint StartPage;
        public PrintDialogResultAction ResultAction;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DevNames
    {
        public ushort DriverOffset;
        public ushort DeviceOffset;
        public ushort OutputOffset;
        public ushort Default;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PrinterInfo4
    {
        public IntPtr PrinterName;
        public IntPtr ServerName;
        public uint Attributes;
    }
}

public sealed class WindowsNativePrintHandoffAdapter : ILinuxNativePrintHandoffAdapter
{
    private readonly LinuxNativePrintCapability _capability;

    public WindowsNativePrintHandoffAdapter(LinuxNativePrintCapability capability)
    {
        _capability = capability ?? throw new ArgumentNullException(nameof(capability));
    }

    public LinuxNativePrintCapability Capability => _capability;

    public async Task<LinuxNativePrintResult> PrintAsync(
        byte[] pdfBytes,
        string documentName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pdfBytes);
        if (!_capability.CanPrint || string.IsNullOrWhiteSpace(_capability.PrinterName))
            return LinuxNativePrintResult.Failed(_capability.Reason);
        if (!HasPdfPayload(pdfBytes))
            return LinuxNativePrintResult.Failed("The printable package is not a valid non-empty PDF.");

        var temporaryPath = Path.Combine(
            Path.GetTempPath(),
            $"freep-print-{Guid.NewGuid():N}.pdf");
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, pdfBytes, cancellationToken).ConfigureAwait(false);
            using var process = StartPrintTo(temporaryPath, _capability.PrinterName);
            if (process is null)
                return LinuxNativePrintResult.Failed("Windows could not start the native PDF print handoff.");

            // Shell print verbs hand the file to the registered PDF application. Wait briefly for
            // the handoff to be accepted, but never kill the application if it remains open.
            await WaitForHandoffAsync(process, cancellationToken).ConfigureAwait(false);
            return LinuxNativePrintResult.Success(process.HasExited ? process.ExitCode : null);
        }
        catch (OperationCanceledException)
        {
            return LinuxNativePrintResult.CanceledResult();
        }
        catch (Win32Exception ex)
        {
            return LinuxNativePrintResult.Failed(
                $"Windows PDF print handoff failed: {ex.Message}", ex.NativeErrorCode);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return LinuxNativePrintResult.Failed(ex.Message);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static Process? StartPrintTo(string path, string printerName)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = path,
            Verb = "printto",
            Arguments = Quote(printerName),
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        return Process.Start(startInfo);
    }

    private static async Task WaitForHandoffAsync(Process process, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // A PDF application may remain open after accepting the print job. Submission is
            // successful once the shell verb has started; do not terminate that external app.
        }
    }

    private static string Quote(string value) =>
        $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private static bool HasPdfPayload(byte[] bytes) =>
        bytes.Length >= 5 && Encoding.ASCII.GetString(bytes, 0, 5) == "%PDF-";

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // The PDF application may still hold the handoff file; the OS will release it later.
        }
    }
}
