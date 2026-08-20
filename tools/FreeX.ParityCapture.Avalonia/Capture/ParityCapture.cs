using System.Text;
using System.IO.Compression;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Free.Shared.AppServices;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

/// <summary>
/// Options for the headless <c>--parity-capture &lt;outDir&gt;</c> mode. This mode renders each app SURFACE
/// (ribbon tabs, the demo grid, every canonical dialog, and each backstage pane) to a PNG using Avalonia's
/// in-process <see cref="global::Avalonia.Media.Imaging.RenderTargetBitmap"/> — no external screenshot tools —
/// so a cross-platform comparison runner can diff the Avalonia shell against the WPF shell. It runs headless
/// in Docker/Xvfb exactly like <see cref="MacOsLaunchSmokeOptions"/> (<c>--launch-smoke</c>): a coordinator
/// hooks <see cref="MainWindow.Opened"/>, waits for shell readiness, captures every surface, writes a
/// <c>manifest.json</c>, then shuts the app down.
///
/// The mode is purely additive: it does not touch the launch-smoke / packaging-smoke code paths or markers.
/// </summary>
internal sealed record ParityCaptureOptions(string OutputDirectory, string? SurfaceId = null)
{
    public const string Argument = "--parity-capture";
    public const string SurfaceArgument = "--parity-capture-surface";

    private const string OutputKey = "output";
    private const string SurfaceKey = "surface";
    private static readonly VisualEvidenceArgumentSpec[] ArgumentSpecs =
    [
        new(
            OutputKey,
            Argument,
            $"{Argument} requires an output directory path.",
            $"{Argument} requires a non-empty output directory path.",
            $"{Argument} was specified more than once."),
        new(
            SurfaceKey,
            SurfaceArgument,
            $"{SurfaceArgument} requires a surface id.",
            $"{SurfaceArgument} requires a non-empty surface id.",
            $"{SurfaceArgument} was specified more than once."),
    ];

    /// <summary>
    /// Parses <c>--parity-capture &lt;outDir&gt;</c> out of <paramref name="args"/>, returning the remaining
    /// (filtered) startup arguments so the app bootstraps normally with whatever workbook args remain. When the
    /// flag is absent, <paramref name="options"/> is null and parsing still succeeds.
    /// </summary>
    public static bool TryParse(
        IReadOnlyList<string> args,
        out ParityCaptureOptions? options,
        out string[] startupArguments,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(args);

        options = null;
        var parsed = VisualEvidenceArgumentParser.Parse(
            args,
            ArgumentSpecs,
            StringComparison.OrdinalIgnoreCase);
        if (parsed.Error is not null)
        {
            startupArguments = [];
            error = parsed.Error;
            return false;
        }

        var outputDirectory = parsed.Value(OutputKey);
        var surfaceId = parsed.Value(SurfaceKey);

        if (outputDirectory is null && surfaceId is not null)
        {
            startupArguments = [];
            error = $"{SurfaceArgument} requires {Argument} to be specified.";
            return false;
        }

        if (outputDirectory is not null)
            options = new ParityCaptureOptions(outputDirectory, surfaceId);

        startupArguments = parsed.RemainingArguments;
        error = "";
        return true;
    }
}

/// <summary>The classification a captured surface falls into, mirrored in the manifest.</summary>
internal enum ParitySurfaceKind
{
    StaticRibbonTab,
    ContextualRibbonTab,
    Screen,
    Grid,
    Dialog,
    Backstage,
    Overlay,
}

/// <summary>One captured (or attempted) surface: id, kind, output PNG name, success flag and a note.</summary>
internal sealed record ParitySurfaceResult(
    string Id,
    ParitySurfaceKind Kind,
    string PngFileName,
    bool Captured,
    string Note,
    int? Width = null,
    int? Height = null,
    string? EvidenceProvenance = null)
{
    public static string KindToken(ParitySurfaceKind kind) => kind switch
    {
        ParitySurfaceKind.StaticRibbonTab => "static-tab",
        ParitySurfaceKind.ContextualRibbonTab => "contextual-tab",
        ParitySurfaceKind.Screen => "screen",
        ParitySurfaceKind.Grid => "grid",
        ParitySurfaceKind.Dialog => "dialog",
        ParitySurfaceKind.Backstage => "backstage",
        ParitySurfaceKind.Overlay => "overlay",
        _ => "unknown",
    };
}

internal readonly record struct ChartPixelBounds(int Left, int Top, int Width, int Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool Contains(int x, int y) =>
        x >= Left && x < Left + Width && y >= Top && y < Top + Height;

    public static ChartPixelBounds FromAbsoluteBounds(
        double left,
        double top,
        double width,
        double height,
        int captureWidth,
        int captureHeight)
    {
        var clampedLeft = Math.Clamp((int)Math.Floor(left), 0, captureWidth);
        var clampedTop = Math.Clamp((int)Math.Floor(top), 0, captureHeight);
        var clampedRight = Math.Clamp((int)Math.Ceiling(left + width), 0, captureWidth);
        var clampedBottom = Math.Clamp((int)Math.Ceiling(top + height), 0, captureHeight);
        return new ChartPixelBounds(clampedLeft, clampedTop, clampedRight - clampedLeft, clampedBottom - clampedTop);
    }
}

internal static class ParityCaptureOutputGuard
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

    public static ParitySurfaceResult ResultForPng(
        string id,
        ParitySurfaceKind kind,
        string outputDirectory,
        string pngFileName,
        int? width = null,
        int? height = null,
        long minimumBytes = 0)
    {
        var pngPath = Path.Combine(outputDirectory, pngFileName);
        var note = ValidatePngOutput(pngPath);
        if (note is null && minimumBytes > 0 && new FileInfo(pngPath).Length < minimumBytes)
            note = $"PNG output is too small to contain the expected rendered surface: {pngPath}";
        return note is null
            ? new ParitySurfaceResult(id, kind, pngFileName, Captured: true, "", width, height)
            : new ParitySurfaceResult(id, kind, pngFileName, Captured: false, note);
    }

    internal static string? ValidatePngOutput(string pngPath)
    {
        if (!File.Exists(pngPath))
            return $"PNG output was not written: {pngPath}";

        var length = new FileInfo(pngPath).Length;
        if (length == 0)
            return $"PNG output is empty: {pngPath}";
        if (length < PngSignature.Length)
            return $"PNG output is too short to be a valid PNG: {pngPath}";

        using var stream = File.OpenRead(pngPath);
        Span<byte> header = stackalloc byte[PngSignature.Length];
        var read = stream.Read(header);
        if (read != PngSignature.Length || !header.SequenceEqual(PngSignature))
            return $"PNG output does not have a valid PNG signature: {pngPath}";

        return null;
    }

    /// <summary>
    /// Validates a range-capture PNG beyond its file signature. A detached Avalonia visual can produce a
    /// syntactically valid, fully transparent-black frame, which is not usable parity evidence.
    /// </summary>
    internal static string? ValidateGridPngOutput(string pngPath, int minimumChromaticPixels = 0)
    {
        var basicValidation = ValidatePngOutput(pngPath);
        if (basicValidation is not null)
            return basicValidation;

        try
        {
            var (rgba, width, height) = DecodeRgbaPng(File.ReadAllBytes(pngPath));
            return ValidateGridPixels(rgba, width, height, minimumChromaticPixels);
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
        {
            return $"Grid PNG output could not be decoded for pixel validation: {pngPath} ({ex.Message})";
        }
    }

    internal static string? ValidateGridPngOutput(
        string pngPath,
        IReadOnlyList<ChartPixelBounds> chartPixelBounds)
    {
        ArgumentNullException.ThrowIfNull(chartPixelBounds);

        var basicValidation = ValidatePngOutput(pngPath);
        if (basicValidation is not null)
            return basicValidation;

        try
        {
            var (rgba, width, height) = DecodeRgbaPng(File.ReadAllBytes(pngPath));
            return ValidateGridPixels(rgba, width, height, chartPixelBounds, minimumChromaticPixels: 64);
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
        {
            return $"Grid PNG output could not be decoded for pixel validation: {pngPath} ({ex.Message})";
        }
    }

    internal static string? ValidateGridPixels(
        ReadOnlySpan<byte> rgba,
        int width,
        int height,
        int minimumChromaticPixels = 0)
    {
        if (width <= 0 || height <= 0 || rgba.Length != checked(width * height * 4))
            return "Grid PNG pixel buffer has invalid dimensions.";

        var firstR = rgba[0];
        var firstG = rgba[1];
        var firstB = rgba[2];
        var firstA = rgba[3];
        var hasVisiblePixel = false;
        var hasVariance = false;
        var chromaticPixelCount = 0;

        for (var index = 0; index < rgba.Length; index += 4)
        {
            hasVisiblePixel |= rgba[index + 3] != 0;
            hasVariance |= rgba[index] != firstR ||
                           rgba[index + 1] != firstG ||
                           rgba[index + 2] != firstB ||
                           rgba[index + 3] != firstA;
            if (rgba[index + 3] != 0)
            {
                var maximum = Math.Max(rgba[index], Math.Max(rgba[index + 1], rgba[index + 2]));
                var minimum = Math.Min(rgba[index], Math.Min(rgba[index + 1], rgba[index + 2]));
                if (maximum - minimum >= 32)
                    chromaticPixelCount++;
            }
        }

        if (!hasVisiblePixel)
            return "Grid PNG output is fully transparent-black.";
        if (!hasVariance)
            return "Grid PNG output has no pixel variance.";
        if (chromaticPixelCount < minimumChromaticPixels)
            return $"Grid PNG output is missing expected chart pixels (found {chromaticPixelCount}, require {minimumChromaticPixels}).";

        return null;
    }

    internal static string? ValidateGridPixels(
        ReadOnlySpan<byte> rgba,
        int width,
        int height,
        IReadOnlyList<ChartPixelBounds> chartPixelBounds,
        int minimumChromaticPixels)
    {
        ArgumentNullException.ThrowIfNull(chartPixelBounds);

        var baselineValidation = ValidateGridPixels(rgba, width, height);
        if (baselineValidation is not null)
            return baselineValidation;
        if (chartPixelBounds.Count == 0)
            return null;

        var chromaticPixelCount = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (!chartPixelBounds.Any(bounds => bounds.Contains(x, y)))
                    continue;

                var index = ((y * width) + x) * 4;
                if (rgba[index + 3] == 0)
                    continue;
                var maximum = Math.Max(rgba[index], Math.Max(rgba[index + 1], rgba[index + 2]));
                var minimum = Math.Min(rgba[index], Math.Min(rgba[index + 1], rgba[index + 2]));
                if (maximum - minimum >= 32)
                    chromaticPixelCount++;
            }
        }

        return chromaticPixelCount < minimumChromaticPixels
            ? $"Grid PNG output is missing expected chart pixels in the chart bounds (found {chromaticPixelCount}, require {minimumChromaticPixels})."
            : null;
    }

    private static (byte[] Rgba, int Width, int Height) DecodeRgbaPng(byte[] png)
    {
        if (png.Length < PngSignature.Length || !png.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature))
            throw new InvalidDataException("The PNG signature is invalid.");

        var offset = PngSignature.Length;
        var width = 0;
        var height = 0;
        byte bitDepth = 0;
        byte colorType = 0;
        using var idat = new MemoryStream();
        while (offset + 12 <= png.Length)
        {
            var length = ReadBigEndianInt32(png, offset);
            offset += 4;
            if (length < 0 || offset + 8 + length > png.Length)
                throw new InvalidDataException("A PNG chunk is truncated.");

            var type = Encoding.ASCII.GetString(png, offset, 4);
            offset += 4;
            var data = png.AsSpan(offset, length);
            offset += length + 4; // skip payload and CRC

            if (type == "IHDR")
            {
                if (length != 13)
                    throw new InvalidDataException("The PNG IHDR chunk is invalid.");
                width = ReadBigEndianInt32(data, 0);
                height = ReadBigEndianInt32(data, 4);
                bitDepth = data[8];
                colorType = data[9];
            }
            else if (type == "IDAT")
            {
                idat.Write(data);
            }
            else if (type == "IEND")
            {
                break;
            }
        }

        if (width <= 0 || height <= 0 || bitDepth != 8 || colorType != 6 || idat.Length == 0)
            throw new NotSupportedException("Expected an 8-bit RGBA PNG.");

        var stride = checked(width * 4);
        var encodedLength = checked(height * (stride + 1));
        using var compressed = new MemoryStream(idat.ToArray());
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        using var decoded = new MemoryStream(encodedLength);
        zlib.CopyTo(decoded);
        var filtered = decoded.ToArray();
        if (filtered.Length != encodedLength)
            throw new InvalidDataException("The PNG pixel data length is invalid.");

        var rgba = new byte[checked(width * height * 4)];
        for (var row = 0; row < height; row++)
        {
            var filter = filtered[row * (stride + 1)];
            var sourceStart = row * (stride + 1) + 1;
            var destinationStart = row * stride;
            var previousStart = destinationStart - stride;
            for (var column = 0; column < stride; column++)
            {
                var left = column >= 4 ? rgba[destinationStart + column - 4] : (byte)0;
                var up = row > 0 ? rgba[previousStart + column] : (byte)0;
                var upLeft = row > 0 && column >= 4 ? rgba[previousStart + column - 4] : (byte)0;
                rgba[destinationStart + column] = filter switch
                {
                    0 => filtered[sourceStart + column],
                    1 => unchecked((byte)(filtered[sourceStart + column] + left)),
                    2 => unchecked((byte)(filtered[sourceStart + column] + up)),
                    3 => unchecked((byte)(filtered[sourceStart + column] + ((left + up) / 2))),
                    4 => unchecked((byte)(filtered[sourceStart + column] + Paeth(left, up, upLeft))),
                    _ => throw new InvalidDataException("The PNG uses an unsupported filter."),
                };
            }
        }

        return (rgba, width, height);
    }

    private static int ReadBigEndianInt32(ReadOnlySpan<byte> bytes, int offset) =>
        (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];

    private static byte Paeth(byte left, byte up, byte upLeft)
    {
        var estimate = left + up - upLeft;
        var leftDistance = Math.Abs(estimate - left);
        var upDistance = Math.Abs(estimate - up);
        var upLeftDistance = Math.Abs(estimate - upLeft);
        return leftDistance <= upDistance && leftDistance <= upLeftDistance
            ? left
            : upDistance <= upLeftDistance
                ? up
                : upLeft;
    }
}

/// <summary>
/// Drives the headless surface capture: hooks <see cref="MainWindow.Opened"/>, waits for shell readiness, then
/// asks the window to render each surface to <c>&lt;outDir&gt;/&lt;surfaceId&gt;.png</c>, writes the manifest,
/// and shuts the desktop lifetime down. Every fallible step is isolated so a single un-capturable surface is
/// recorded with <c>captured:false</c> + a reason rather than aborting the whole run.
/// </summary>
internal static class ParityCaptureCoordinator
{
    private const int ShellReadyWaitMilliseconds = 15000;
    private const int PollDelayMilliseconds = 100;

    // Hard backstop: if the graceful desktop shutdown is blocked for any reason (a lingering
    // window/dialog, a dirty-workbook prompt, etc.) force-exit so the headless capture process can
    // never hang the Docker container after the manifest is already written.
    private const int ShutdownBackstopMilliseconds = 8000;

    public static void Start(MainWindow mainWindow, ParityCaptureOptions options, LocalAppDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(mainWindow);
        ArgumentNullException.ThrowIfNull(options);

        mainWindow.Opened += async (_, _) => await RunAsync(mainWindow, options, diagnostics);
    }

    private static async Task RunAsync(MainWindow mainWindow, ParityCaptureOptions options, LocalAppDiagnostics? diagnostics)
    {
        diagnostics?.RecordEvent("parity_capture", new Dictionary<string, string?>
        {
            ["source"] = "parity_capture",
            ["scope"] = "launch",
            ["status"] = "starting",
        });

        IReadOnlyList<ParitySurfaceResult> results;
        try
        {
            Directory.CreateDirectory(options.OutputDirectory);
            await WaitForShellReadyAsync(mainWindow);
            results = await mainWindow.CaptureParitySurfacesAsync(
                options.OutputDirectory,
                targetSurfaceId: options.SurfaceId);
            WriteManifest(options.OutputDirectory, results);
            diagnostics?.RecordEvent("parity_capture", new Dictionary<string, string?>
            {
                ["source"] = "parity_capture",
                ["scope"] = "launch",
                ["status"] = "completed",
                ["captured"] = results.Count(r => r.Captured).ToString(),
                ["total"] = results.Count.ToString(),
            });
            // The capture seeds/edits the demo workbook, so it is dirty by now. Allow the close
            // without the save prompt that would otherwise cancel shutdown and hang under Xvfb.
            mainWindow.AllowCloseWithoutDirtyPromptForParityCapture();
            Shutdown(0);
        }
        catch (Exception ex)
        {
            diagnostics?.RecordCrash(ex, "parity_capture");
            TryWriteFailureManifest(options.OutputDirectory, ex);
            mainWindow.AllowCloseWithoutDirtyPromptForParityCapture();
            Shutdown(1);
        }
    }

    private static async Task WaitForShellReadyAsync(MainWindow mainWindow)
    {
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(ShellReadyWaitMilliseconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var shell = mainWindow.CreateRendererValidationAccess().ObserveShell();
            if (shell.WindowShown && !shell.IsOpening && shell.ViewportRowCount > 0)
                return;
            await Task.Delay(PollDelayMilliseconds);
        }
        // Fall through: capture proceeds best-effort even if the readiness heuristic never flipped.
    }

    /// <summary>
    /// Serializes the manifest with the EXACT contract the comparison runner depends on:
    /// <c>{ "platform", "shell": "avalonia", "surfaces": [ { "id", "kind", "png", "captured", "note",
    /// "width", "height", "evidenceProvenance" } ] }</c>.
    /// Hand-rolled (no JSON dependency) so the portable services tier stays untouched and the output is stable.
    /// </summary>
    private static void WriteManifest(string outputDirectory, IReadOnlyList<ParitySurfaceResult> results)
    {
        var manifest = new
        {
            platform = PlatformName(),
            shell = "avalonia",
            surfaces = results.Select(result => new
            {
                id = result.Id,
                kind = ParitySurfaceResult.KindToken(result.Kind),
                png = result.PngFileName,
                captured = result.Captured,
                note = result.Note,
                width = result.Width,
                height = result.Height,
                evidenceProvenance = result.EvidenceProvenance ?? string.Empty,
            }),
        };
        VisualEvidenceManifestIO.Write(
            Path.Combine(outputDirectory, "manifest.json"),
            manifest,
            VisualEvidenceManifestIO.CreateJsonOptions(camelCase: false, stringEnums: false));
    }

    private static void TryWriteFailureManifest(string outputDirectory, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(outputDirectory);
            var manifest = new
            {
                platform = PlatformName(),
                shell = "avalonia",
                surfaces = Array.Empty<object>(),
                error = $"{ex.GetType().Name}: {ex.Message}",
            };
            VisualEvidenceManifestIO.Write(
                Path.Combine(outputDirectory, "manifest.json"),
                manifest,
                VisualEvidenceManifestIO.CreateJsonOptions(camelCase: false, stringEnums: false));
        }
        catch
        {
            // Best-effort: if even the failure manifest cannot be written there is nothing more to do.
        }
    }

    private static string PlatformName()
    {
        if (OperatingSystem.IsWindows())
            return "windows";
        if (OperatingSystem.IsMacOS())
            return "macos";
        if (OperatingSystem.IsLinux())
            return "linux";
        return "unknown";
    }

    private static void Shutdown(int exitCode)
    {
        // Defer the shutdown to the next dispatcher turn: the capture runs from the window's Opened handler,
        // and tearing the classic-desktop lifetime down while that handler is still on the stack trips a
        // NullReferenceException inside Avalonia's lifetime start path. Posting lets the open complete first.
        Dispatcher.UIThread.Post(() =>
        {
            if (global::Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.TryShutdown(exitCode);
        }, DispatcherPriority.Background);

        // Backstop on a background thread: if the graceful shutdown above is still blocked after a
        // grace period, terminate the process outright. The PNGs + manifest are already flushed, so
        // a hard exit is safe and guarantees the capture container can exit.
        _ = Task.Run(async () =>
        {
            await Task.Delay(ShutdownBackstopMilliseconds).ConfigureAwait(false);
            Environment.Exit(exitCode);
        });
    }
}

/// <summary>
/// Options for the headless <c>--parity-grid &lt;fixture.xlsx&gt; &lt;A1:Range&gt; &lt;outDir&gt;
/// [--parity-grid-sheet &lt;worksheet&gt;]</c> mode.
/// Renders a specific cell range of a workbook to a PNG using Avalonia's in-process
/// <see cref="global::Avalonia.Media.Imaging.RenderTargetBitmap"/> — cropped to the exact pixel extent of the
/// requested range with no row/column header chrome — so it can be diffed against the WPF/Excel
/// <c>--capture-range</c> output from <c>FreeX.SheetGridImageCompare</c>.
///
/// Runs headless (same bootstrap as <c>--parity-capture</c>) and emits a small JSON result alongside the PNG:
/// <c>{ "png", "widthPx", "heightPx", "sheet", "range" }</c>.
/// </summary>
internal sealed record GridCaptureOptions(
    string WorkbookPath,
    string RangeText,
    string OutputDirectory,
    string? WorksheetName = null)
{
    public const string Argument = "--parity-grid";
    public const string SheetArgument = "--parity-grid-sheet";

    private static bool IsGridArgument(string argument) =>
        string.Equals(argument, Argument, StringComparison.OrdinalIgnoreCase);

    private static bool IsSheetArgument(string argument) =>
        string.Equals(argument, SheetArgument, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Parses <c>--parity-grid &lt;xlsx&gt; &lt;range&gt; &lt;outDir&gt;
    /// [--parity-grid-sheet &lt;worksheet&gt;]</c> out of <paramref name="args"/>,
    /// returning remaining (filtered) startup args. When the flag is absent, <paramref name="options"/> is null
    /// and parsing still succeeds.
    /// </summary>
    public static bool TryParse(
        IReadOnlyList<string> args,
        out GridCaptureOptions? options,
        out string[] startupArguments,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(args);

        options = null;
        error = "";
        var filteredArguments = new List<string>();
        string? workbookPath = null;
        string? rangeText = null;
        string? outputDirectory = null;
        string? worksheetName = null;
        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            if (IsGridArgument(argument))
            {
                if (workbookPath is not null)
                {
                    startupArguments = [];
                    error = $"{Argument} was specified more than once.";
                    return false;
                }

                // Expect exactly three positional values after the flag: <xlsx> <range> <outDir>
                if (index + 3 >= args.Count)
                {
                    startupArguments = [];
                    error = $"{Argument} requires three arguments: <workbook.xlsx> <A1:Range> <outDir>.";
                    return false;
                }

                workbookPath = args[++index];
                rangeText = args[++index];
                outputDirectory = args[++index];

                if (string.IsNullOrWhiteSpace(workbookPath) ||
                    string.IsNullOrWhiteSpace(rangeText) ||
                    string.IsNullOrWhiteSpace(outputDirectory))
                {
                    startupArguments = [];
                    error = $"{Argument}: none of <workbook.xlsx>, <A1:Range>, <outDir> may be blank.";
                    return false;
                }

                continue;
            }

            if (IsSheetArgument(argument))
            {
                if (worksheetName is not null)
                {
                    startupArguments = [];
                    error = $"{SheetArgument} was specified more than once.";
                    return false;
                }

                if (index + 1 >= args.Count || string.IsNullOrWhiteSpace(args[index + 1]))
                {
                    startupArguments = [];
                    error = $"{SheetArgument} requires a non-empty worksheet name.";
                    return false;
                }

                worksheetName = args[++index];
                continue;
            }

            filteredArguments.Add(argument);
        }

        if (worksheetName is not null && workbookPath is null)
        {
            startupArguments = [];
            error = $"{SheetArgument} requires {Argument} to be specified.";
            return false;
        }

        if (workbookPath is not null)
            options = new GridCaptureOptions(workbookPath, rangeText!, outputDirectory!, worksheetName);

        startupArguments = filteredArguments.ToArray();
        return true;
    }
}

/// <summary>
/// Drives the headless grid-range capture: hooks <see cref="MainWindow.Opened"/>, waits for shell readiness,
/// then delegates to <see cref="MainWindow.CaptureGridRangeAsync"/> to load the target workbook, build just the
/// grid sub-tree for the requested range (no ribbon/chrome), render it to a PNG, write a JSON log, and shut down.
/// </summary>
internal static class GridCaptureCoordinator
{
    private const int ShellReadyWaitMilliseconds = 15000;
    private const int PollDelayMilliseconds = 100;

    public static void Start(MainWindow mainWindow, GridCaptureOptions options, LocalAppDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(mainWindow);
        ArgumentNullException.ThrowIfNull(options);

        mainWindow.Opened += async (_, _) => await RunAsync(mainWindow, options, diagnostics);
    }

    private static async Task RunAsync(MainWindow mainWindow, GridCaptureOptions options, LocalAppDiagnostics? diagnostics)
    {
        diagnostics?.RecordEvent("grid_capture", new Dictionary<string, string?>
        {
            ["source"] = "grid_capture",
            ["scope"] = "launch",
            ["status"] = "starting",
        });

        try
        {
            Directory.CreateDirectory(options.OutputDirectory);
            await WaitForShellReadyAsync(mainWindow);
            var result = await mainWindow.CaptureGridRangeAsync(
                options.WorkbookPath,
                options.RangeText,
                options.OutputDirectory,
                options.WorksheetName);

            Console.WriteLine(result.JsonLog);

            diagnostics?.RecordEvent("grid_capture", new Dictionary<string, string?>
            {
                ["source"] = "grid_capture",
                ["scope"] = "launch",
                ["status"] = result.Captured ? "completed" : "failed",
                ["png"] = result.PngPath,
                ["note"] = result.Note,
            });

            Shutdown(result.Captured ? 0 : 1);
        }
        catch (Exception ex)
        {
            diagnostics?.RecordCrash(ex, "grid_capture");
            Console.Error.WriteLine($"grid-capture failed: {ex.GetType().Name}: {ex.Message}");
            Shutdown(1);
        }
    }

    private static async Task WaitForShellReadyAsync(MainWindow mainWindow)
    {
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(ShellReadyWaitMilliseconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var shell = mainWindow.CreateRendererValidationAccess().ObserveShell();
            if (shell.WindowShown && !shell.IsOpening && shell.ViewportRowCount > 0)
                return;
            await Task.Delay(PollDelayMilliseconds);
        }
    }

    private static void Shutdown(int exitCode)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (global::Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.TryShutdown(exitCode);
        }, DispatcherPriority.Background);
    }
}

/// <summary>Result returned by <see cref="MainWindow.CaptureGridRangeAsync"/>.</summary>
internal sealed record GridCaptureResult(
    bool Captured,
    string PngPath,
    string PngFileName,
    int WidthPx,
    int HeightPx,
    string SheetName,
    string RangeText,
    string Note)
{
    /// <summary>
    /// One-line JSON suitable for stdout so CI scripts can parse dimensions without reading the PNG.
    /// <c>{ "png": "...", "widthPx": 123, "heightPx": 456, "sheet": "Sheet1", "range": "A1:B15" }</c>
    /// </summary>
    public string JsonLog =>
        $"{{ \"captured\": {(Captured ? "true" : "false")}, \"png\": {JsonString(PngPath)}, " +
        $"\"widthPx\": {WidthPx}, \"heightPx\": {HeightPx}, " +
        $"\"sheet\": {JsonString(SheetName)}, \"range\": {JsonString(RangeText)}, " +
        $"\"note\": {JsonString(Note)} }}";

    private static string JsonString(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (ch < 0x20) sb.Append("\\u").Append(((int)ch).ToString("x4"));
                    else sb.Append(ch);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }
}
