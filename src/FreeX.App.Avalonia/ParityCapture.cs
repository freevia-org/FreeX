using System.Text;
using System.Globalization;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
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

    private static bool IsCaptureArgument(string argument) =>
        string.Equals(argument, Argument, StringComparison.OrdinalIgnoreCase);

    private static bool IsSurfaceArgument(string argument) =>
        string.Equals(argument, SurfaceArgument, StringComparison.OrdinalIgnoreCase);

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
        error = "";
        var filteredArguments = new List<string>();
        string? outputDirectory = null;
        string? surfaceId = null;
        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            if (IsSurfaceArgument(argument))
            {
                if (surfaceId is not null)
                {
                    startupArguments = [];
                    error = $"{SurfaceArgument} was specified more than once.";
                    return false;
                }

                if (index + 1 >= args.Count)
                {
                    startupArguments = [];
                    error = $"{SurfaceArgument} requires a surface id.";
                    return false;
                }

                surfaceId = args[++index];
                if (string.IsNullOrWhiteSpace(surfaceId))
                {
                    startupArguments = [];
                    error = $"{SurfaceArgument} requires a non-empty surface id.";
                    return false;
                }

                continue;
            }

            if (!IsCaptureArgument(argument))
            {
                filteredArguments.Add(argument);
                continue;
            }

            if (outputDirectory is not null)
            {
                startupArguments = [];
                error = $"{Argument} was specified more than once.";
                return false;
            }

            if (index + 1 >= args.Count)
            {
                startupArguments = [];
                error = $"{Argument} requires an output directory path.";
                return false;
            }

            outputDirectory = args[++index];
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                startupArguments = [];
                error = $"{Argument} requires a non-empty output directory path.";
                return false;
            }
        }

        if (outputDirectory is null && surfaceId is not null)
        {
            startupArguments = [];
            error = $"{SurfaceArgument} requires {Argument} to be specified.";
            return false;
        }

        if (outputDirectory is not null)
            options = new ParityCaptureOptions(outputDirectory, surfaceId);

        startupArguments = filteredArguments.ToArray();
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
    int? Height = null)
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

    public static void Start(MainWindow mainWindow, ParityCaptureOptions options, AvaloniaAppDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(mainWindow);
        ArgumentNullException.ThrowIfNull(options);

        mainWindow.Opened += async (_, _) => await RunAsync(mainWindow, options, diagnostics);
    }

    private static async Task RunAsync(MainWindow mainWindow, ParityCaptureOptions options, AvaloniaAppDiagnostics? diagnostics)
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
            var snapshot = mainWindow.CreateLaunchSmokeSnapshot();
            if (snapshot.WindowShown && !snapshot.IsOpening && snapshot.ViewportRowCount > 0)
                return;
            await Task.Delay(PollDelayMilliseconds);
        }
        // Fall through: capture proceeds best-effort even if the readiness heuristic never flipped.
    }

    /// <summary>
    /// Serializes the manifest with the EXACT contract the comparison runner depends on:
    /// <c>{ "platform", "shell": "avalonia", "surfaces": [ { "id", "kind", "png", "captured", "note" } ] }</c>.
    /// Hand-rolled (no JSON dependency) so the portable services tier stays untouched and the output is stable.
    /// </summary>
    private static void WriteManifest(string outputDirectory, IReadOnlyList<ParitySurfaceResult> results)
    {
        var builder = new StringBuilder();
        builder.Append("{\n");
        builder.Append("  \"platform\": ").Append(JsonString(PlatformName())).Append(",\n");
        builder.Append("  \"shell\": \"avalonia\",\n");
        builder.Append("  \"surfaces\": [\n");
        for (var i = 0; i < results.Count; i++)
        {
            var r = results[i];
            builder.Append("    { ");
            builder.Append("\"id\": ").Append(JsonString(r.Id)).Append(", ");
            builder.Append("\"kind\": ").Append(JsonString(ParitySurfaceResult.KindToken(r.Kind))).Append(", ");
            builder.Append("\"png\": ").Append(JsonString(r.PngFileName)).Append(", ");
            builder.Append("\"captured\": ").Append(r.Captured ? "true" : "false").Append(", ");
            builder.Append("\"note\": ").Append(JsonString(r.Note)).Append(", ");
            builder.Append("\"width\": ").Append(r.Width?.ToString(CultureInfo.InvariantCulture) ?? "null").Append(", ");
            builder.Append("\"height\": ").Append(r.Height?.ToString(CultureInfo.InvariantCulture) ?? "null").Append(" }");
            builder.Append(i < results.Count - 1 ? ",\n" : "\n");
        }
        builder.Append("  ]\n");
        builder.Append("}\n");
        File.WriteAllText(Path.Combine(outputDirectory, "manifest.json"), builder.ToString());
    }

    private static void TryWriteFailureManifest(string outputDirectory, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(outputDirectory);
            var builder = new StringBuilder();
            builder.Append("{\n");
            builder.Append("  \"platform\": ").Append(JsonString(PlatformName())).Append(",\n");
            builder.Append("  \"shell\": \"avalonia\",\n");
            builder.Append("  \"surfaces\": [],\n");
            builder.Append("  \"error\": ").Append(JsonString($"{ex.GetType().Name}: {ex.Message}")).Append('\n');
            builder.Append("}\n");
            File.WriteAllText(Path.Combine(outputDirectory, "manifest.json"), builder.ToString());
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

    private static string JsonString(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (ch < 0x20)
                        builder.Append("\\u").Append(((int)ch).ToString("x4"));
                    else
                        builder.Append(ch);
                    break;
            }
        }
        builder.Append('"');
        return builder.ToString();
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
/// Options for the headless <c>--parity-grid &lt;fixture.xlsx&gt; &lt;A1:Range&gt; &lt;outDir&gt;</c> mode.
/// Renders a specific cell range of a workbook to a PNG using Avalonia's in-process
/// <see cref="global::Avalonia.Media.Imaging.RenderTargetBitmap"/> — cropped to the exact pixel extent of the
/// requested range with no row/column header chrome — so it can be diffed against the WPF/Excel
/// <c>--capture-range</c> output from <c>FreeX.SheetGridImageCompare</c>.
///
/// Runs headless (same bootstrap as <c>--parity-capture</c>) and emits a small JSON result alongside the PNG:
/// <c>{ "png", "widthPx", "heightPx", "sheet", "range" }</c>.
/// </summary>
internal sealed record GridCaptureOptions(string WorkbookPath, string RangeText, string OutputDirectory)
{
    public const string Argument = "--parity-grid";

    private static bool IsArgument(string argument) =>
        string.Equals(argument, Argument, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Parses <c>--parity-grid &lt;xlsx&gt; &lt;range&gt; &lt;outDir&gt;</c> out of <paramref name="args"/>,
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
        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            if (!IsArgument(argument))
            {
                filteredArguments.Add(argument);
                continue;
            }

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
            var rangeText = args[++index];
            var outputDirectory = args[++index];

            if (string.IsNullOrWhiteSpace(workbookPath) ||
                string.IsNullOrWhiteSpace(rangeText) ||
                string.IsNullOrWhiteSpace(outputDirectory))
            {
                startupArguments = [];
                error = $"{Argument}: none of <workbook.xlsx>, <A1:Range>, <outDir> may be blank.";
                return false;
            }

            options = new GridCaptureOptions(workbookPath, rangeText, outputDirectory);
        }

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

    public static void Start(MainWindow mainWindow, GridCaptureOptions options, AvaloniaAppDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(mainWindow);
        ArgumentNullException.ThrowIfNull(options);

        mainWindow.Opened += async (_, _) => await RunAsync(mainWindow, options, diagnostics);
    }

    private static async Task RunAsync(MainWindow mainWindow, GridCaptureOptions options, AvaloniaAppDiagnostics? diagnostics)
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
                options.OutputDirectory);

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
            var snapshot = mainWindow.CreateLaunchSmokeSnapshot();
            if (snapshot.WindowShown && !snapshot.IsOpening && snapshot.ViewportRowCount > 0)
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
