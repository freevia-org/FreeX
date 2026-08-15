using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Free.Shared.AppServices.Printing;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Avalonia.Editing;

internal interface IScreenClipService
{
    Task<ScreenClipCapture?> CaptureAsync(
        Window owner,
        CancellationToken cancellationToken = default);
}

/// <summary>Production overlay/capture workflow for Windows, macOS, X11, and Wayland hosts.</summary>
internal sealed class AvaloniaScreenClipService : IScreenClipService
{
    private readonly IProcessRunner _processRunner;

    public AvaloniaScreenClipService(IProcessRunner? processRunner = null)
    {
        _processRunner = processRunner ?? new SystemProcessRunner();
    }

    public async Task<ScreenClipCapture?> CaptureAsync(
        Window owner,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var screens = owner.Screens?.All;
        if (screens is null || screens.Count == 0)
            throw new InvalidOperationException("No display is available for screen clipping.");

        var bounds = Union(screens.Select(screen => screen.Bounds));
        var scale = owner.Screens?.ScreenFromWindow(owner)?.Scaling
            ?? owner.RenderScaling;
        if (!double.IsFinite(scale) || scale <= 0)
            scale = 1;

        var previousState = owner.WindowState;
        try
        {
            owner.WindowState = WindowState.Minimized;
            await Dispatcher.UIThread.InvokeAsync(
                static () => { },
                DispatcherPriority.ApplicationIdle);
            await Task.Delay(150, cancellationToken);

            var overlay = new ScreenClipOverlay(bounds, scale);
            var region = await overlay.ShowSelectionAsync();
            if (region is not { } selected || selected.IsEmpty)
                return null;

            var png = await CaptureRegionPngAsync(selected, cancellationToken);
            return png.Length == 0
                ? null
                : new ScreenClipCapture(png, selected.Width, selected.Height);
        }
        finally
        {
            owner.WindowState = previousState;
            owner.Activate();
        }
    }

    private async Task<byte[]> CaptureRegionPngAsync(
        ScreenPixelRect region,
        CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
            return WindowsScreenCapture.CapturePng(region);

        using var outputFile = TemporaryFileLease.CreateForExternalWriter(
            "freew-screen-clip-",
            ".png");
        var outputPath = outputFile.Path;
        var attempts = OperatingSystem.IsMacOS()
            ? MacCaptureAttempts(region, outputPath)
            : LinuxCaptureAttempts(region, outputPath);
        var failures = new List<string>();
        foreach (var attempt in attempts)
        {
            try
            {
                var result = await _processRunner.RunAsync(attempt, cancellationToken);
                if (result.Succeeded && File.Exists(outputPath))
                {
                    var bytes = await FileByteReadWorkflow.ReadLocalPathBytesAsync(
                        outputPath,
                        cancellationToken);
                    if (bytes.Length > 0)
                        return bytes;
                }

                failures.Add($"{attempt.FileName}: {result.StandardError.Trim()}");
            }
            catch (Win32Exception ex)
            {
                failures.Add($"{attempt.FileName}: {ex.Message}");
            }
        }

        throw new InvalidOperationException(
            "No screen capture backend succeeded. " + string.Join("; ", failures));
    }

    private static IReadOnlyList<ProcessInvocation> MacCaptureAttempts(
        ScreenPixelRect region,
        string outputPath) =>
    [
        new(
            "/usr/sbin/screencapture",
            ["-x", $"-R{region.X},{region.Y},{region.Width},{region.Height}", outputPath]),
    ];

    private static IReadOnlyList<ProcessInvocation> LinuxCaptureAttempts(
        ScreenPixelRect region,
        string outputPath)
    {
        var attempts = new List<ProcessInvocation>();
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
        {
            attempts.Add(new ProcessInvocation(
                "grim",
                ["-g", $"{region.X},{region.Y} {region.Width}x{region.Height}", outputPath]));
        }

        attempts.Add(new ProcessInvocation(
            "scrot",
            ["-o", "-a", $"{region.X},{region.Y},{region.Width},{region.Height}", outputPath]));
        attempts.Add(new ProcessInvocation(
            "import",
            [
                "-window", "root",
                "-crop", $"{region.Width}x{region.Height}+{region.X}+{region.Y}",
                outputPath,
            ]));
        return attempts;
    }

    private static PixelRect Union(IEnumerable<PixelRect> source)
    {
        using var enumerator = source.GetEnumerator();
        if (!enumerator.MoveNext())
            throw new InvalidOperationException("No display bounds are available.");

        var first = enumerator.Current;
        var left = first.X;
        var top = first.Y;
        var right = first.Right;
        var bottom = first.Bottom;
        while (enumerator.MoveNext())
        {
            var current = enumerator.Current;
            left = Math.Min(left, current.X);
            top = Math.Min(top, current.Y);
            right = Math.Max(right, current.Right);
            bottom = Math.Max(bottom, current.Bottom);
        }

        return new PixelRect(left, top, right - left, bottom - top);
    }
}

internal static class WindowsScreenCapture
{
    private const int Srccopy = 0x00CC0020;
    private const uint DibRgbColors = 0;

    public static byte[] CapturePng(ScreenPixelRect region)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException();
        if (region.IsEmpty)
            return [];

        var screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        var memoryDc = CreateCompatibleDC(screenDc);
        var bitmap = CreateCompatibleBitmap(screenDc, region.Width, region.Height);
        var previous = IntPtr.Zero;
        try
        {
            if (memoryDc == IntPtr.Zero || bitmap == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            previous = SelectObject(memoryDc, bitmap);
            if (!BitBlt(
                    memoryDc,
                    0,
                    0,
                    region.Width,
                    region.Height,
                    screenDc,
                    region.X,
                    region.Y,
                    Srccopy))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            var info = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                    Width = region.Width,
                    Height = -region.Height,
                    Planes = 1,
                    BitCount = 32,
                },
            };
            var pixels = new byte[checked(region.Width * region.Height * 4)];
            var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                if (GetDIBits(
                        memoryDc,
                        bitmap,
                        0,
                        (uint)region.Height,
                        handle.AddrOfPinnedObject(),
                        ref info,
                        DibRgbColors) == 0)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
            finally
            {
                handle.Free();
            }

            return EncodeBgraPng(pixels, region.Width, region.Height);
        }
        finally
        {
            if (previous != IntPtr.Zero && memoryDc != IntPtr.Zero)
                SelectObject(memoryDc, previous);
            if (bitmap != IntPtr.Zero)
                DeleteObject(bitmap);
            if (memoryDc != IntPtr.Zero)
                DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static byte[] EncodeBgraPng(byte[] pixels, int width, int height)
    {
        using var bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);
        using (var framebuffer = bitmap.Lock())
        {
            var sourceStride = width * 4;
            for (var row = 0; row < height; row++)
            {
                Marshal.Copy(
                    pixels,
                    row * sourceStride,
                    IntPtr.Add(framebuffer.Address, row * framebuffer.RowBytes),
                    sourceStride);
            }
        }

        using var output = new MemoryStream();
        bitmap.Save(output);
        return output.ToArray();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Colors;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteDC(IntPtr dc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool BitBlt(
        IntPtr destination,
        int xDestination,
        int yDestination,
        int width,
        int height,
        IntPtr source,
        int xSource,
        int ySource,
        int rasterOperation);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int GetDIBits(
        IntPtr dc,
        IntPtr bitmap,
        uint startScan,
        uint scanLines,
        IntPtr bits,
        ref BitmapInfo info,
        uint usage);
}
