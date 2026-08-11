using System.Runtime.InteropServices;
using System.Text;
using Free.Shared.AppServices;
using Free.Shared.AppServices.Printing;
using Free.Shared.AppServices.Windows;
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
    private static readonly IPlatformPrintService PrintService = CreatePrintService();

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
            var canMuxTimedCaptions = WindowsNativeVideoExportAdapter.CanUseCaptionFallback;

            return new LinuxVideoEncoderCapability(
                CanEncodeMp4: true,
                ExecutablePath: WindowsNativeVideoExportAdapter.ExecutablePath,
                EncoderName: "Windows MediaComposition",
                CanCaptureNarration: hasMicrophone,
                Reason: BuildWindowsVideoCapabilityReason(hasMicrophone, hasCamera, canMuxTimedCaptions),
                CanCaptureCameraAndMedia: hasCamera,
                CanMuxTimedCaptions: canMuxTimedCaptions);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            var canMuxTimedCaptions = WindowsNativeVideoExportAdapter.CanUseCaptionFallback;
            return new LinuxVideoEncoderCapability(
                CanEncodeMp4: true,
                ExecutablePath: WindowsNativeVideoExportAdapter.ExecutablePath,
                EncoderName: "Windows MediaComposition",
                CanCaptureNarration: false,
                Reason: BuildWindowsVideoCapabilityReason(
                    hasMicrophone: false,
                    hasCamera: false,
                    canMuxTimedCaptions: canMuxTimedCaptions) + $" Device detection failed: {ex.Message}",
                CanCaptureCameraAndMedia: false,
                CanMuxTimedCaptions: canMuxTimedCaptions);
        }
    }

    private static string BuildWindowsVideoCapabilityReason(
        bool hasMicrophone,
        bool hasCamera,
        bool canMuxTimedCaptions) =>
        (hasMicrophone, hasCamera) switch
        {
            (true, true) => AppendTimedCaptionReason("Windows MediaComposition video export, delayed multi-track narration, and captured camera PIP are available.", canMuxTimedCaptions),
            (true, false) => AppendTimedCaptionReason("Windows MediaComposition video export and narration capture are available; no camera device is currently available for camera PIP.", canMuxTimedCaptions),
            (false, true) => AppendTimedCaptionReason("Windows MediaComposition video export and camera PIP are available; no microphone device is currently available for narration.", canMuxTimedCaptions),
            _ => AppendTimedCaptionReason("Windows MediaComposition video export is available; no microphone device is currently available for narration, and no camera device is currently available for camera PIP.", canMuxTimedCaptions)
        };

    private static string AppendTimedCaptionReason(string reason, bool canMuxTimedCaptions) =>
        canMuxTimedCaptions
            ? $"{reason} Timed captions use the available ffmpeg mov_text fallback."
            : reason;

    public static LinuxNativePrintCapability DetectPrint()
    {
        if (!OperatingSystem.IsWindows())
            return LinuxNativePrintCapability.Unavailable(
                "Windows native printing is available only on Windows.");

        var discovery = PrintService.DiscoverAsync().GetAwaiter().GetResult();
        var printer = discovery.DefaultPrinter;
        return string.IsNullOrWhiteSpace(printer)
            ? LinuxNativePrintCapability.Unavailable(
                discovery.Message ?? "Windows reported no default printer queue.")
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

        var knownPrinters = PrintService.DiscoverAsync().GetAwaiter().GetResult().Printers;
        return knownPrinters.Any(printer =>
                string.Equals(printer.Name, normalized, StringComparison.OrdinalIgnoreCase))
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

        return PrintService.DiscoverAsync().GetAwaiter().GetResult().Printers
            .Select(static printer => printer.Name)
            .ToArray();
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

    private static WindowsPrintService CreatePrintService() =>
        new(options: new WindowsPrintServiceOptions(
            RequirePrinterDiscoveryBeforeSubmission: false,
            RejectNonZeroHandlerExitCode: false));

    public static ILinuxVideoExportAdapter CreateVideoAdapter(
        LinuxVideoEncoderCapability capability) =>
        string.Equals(capability.ExecutablePath, WindowsNativeVideoExportAdapter.ExecutablePath, StringComparison.Ordinal)
            ? new WindowsNativeVideoExportAdapter(capability)
            : new LinuxVideoExportAdapter(capability);

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

}

public sealed class WindowsNativePrintHandoffAdapter : ILinuxNativePrintHandoffAdapter
{
    private readonly LinuxNativePrintCapability _capability;
    private readonly IPlatformPrintService _printService;

    public WindowsNativePrintHandoffAdapter(
        LinuxNativePrintCapability capability,
        IPlatformPrintService? printService = null)
    {
        _capability = capability ?? throw new ArgumentNullException(nameof(capability));
        _printService = printService ?? new WindowsPrintService(
            options: new WindowsPrintServiceOptions(
                RequirePrinterDiscoveryBeforeSubmission: false,
                RejectNonZeroHandlerExitCode: false));
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

        try
        {
            using var temporaryFile = TemporaryFileLease.Create("freep-print-", ".pdf");
            var temporaryPath = temporaryFile.Path;
            await temporaryFile.WriteAllBytesAsync(pdfBytes, cancellationToken).ConfigureAwait(false);
            var submission = await _printService.SubmitAsync(
                temporaryPath,
                new PrintSelection(
                    _capability.PrinterName,
                    JobTitle: documentName),
                cancellationToken).ConfigureAwait(false);
            return submission.Status switch
            {
                PrintSubmissionStatus.Submitted =>
                    LinuxNativePrintResult.Success(submission.NativeExitCode),
                PrintSubmissionStatus.Cancelled =>
                    LinuxNativePrintResult.CanceledResult(),
                _ => LinuxNativePrintResult.Failed(
                    submission.Message ?? "Windows could not start the native PDF print handoff.",
                    submission.NativeErrorCode ?? submission.NativeExitCode),
            };
        }
        catch (OperationCanceledException)
        {
            return LinuxNativePrintResult.CanceledResult();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return LinuxNativePrintResult.Failed(ex.Message);
        }
    }

    private static bool HasPdfPayload(byte[] bytes) =>
        bytes.Length >= 5 && Encoding.ASCII.GetString(bytes, 0, 5) == "%PDF-";

}
