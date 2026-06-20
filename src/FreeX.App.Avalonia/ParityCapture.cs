using System.Text;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using FreeX.App.Services;

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
internal sealed record ParityCaptureOptions(string OutputDirectory)
{
    public const string Argument = "--parity-capture";

    private static bool IsArgument(string argument) =>
        string.Equals(argument, Argument, StringComparison.OrdinalIgnoreCase);

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
        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            if (!IsArgument(argument))
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

        if (outputDirectory is not null)
            options = new ParityCaptureOptions(outputDirectory);

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
}

/// <summary>One captured (or attempted) surface: id, kind, output PNG name, success flag and a note.</summary>
internal sealed record ParitySurfaceResult(
    string Id,
    ParitySurfaceKind Kind,
    string PngFileName,
    bool Captured,
    string Note)
{
    public static string KindToken(ParitySurfaceKind kind) => kind switch
    {
        ParitySurfaceKind.StaticRibbonTab => "static-tab",
        ParitySurfaceKind.ContextualRibbonTab => "contextual-tab",
        ParitySurfaceKind.Screen => "screen",
        ParitySurfaceKind.Grid => "grid",
        ParitySurfaceKind.Dialog => "dialog",
        ParitySurfaceKind.Backstage => "backstage",
        _ => "unknown",
    };
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
            results = await mainWindow.CaptureParitySurfacesAsync(options.OutputDirectory);
            WriteManifest(options.OutputDirectory, results);
            diagnostics?.RecordEvent("parity_capture", new Dictionary<string, string?>
            {
                ["source"] = "parity_capture",
                ["scope"] = "launch",
                ["status"] = "completed",
                ["captured"] = results.Count(r => r.Captured).ToString(),
                ["total"] = results.Count.ToString(),
            });
            Shutdown(0);
        }
        catch (Exception ex)
        {
            diagnostics?.RecordCrash(ex, "parity_capture");
            TryWriteFailureManifest(options.OutputDirectory, ex);
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
            builder.Append("\"note\": ").Append(JsonString(r.Note)).Append(" }");
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
    }
}
