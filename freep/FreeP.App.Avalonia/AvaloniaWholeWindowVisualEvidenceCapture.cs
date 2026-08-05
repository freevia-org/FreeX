using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Free.Shared.Drawing;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia;

internal static class AvaloniaWholeWindowVisualEvidenceCapture
{
    internal const string OutputArgument = "--whole-window-visual-evidence-output";
    internal const string ScenarioArgument = "--whole-window-visual-evidence-scenario";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    internal static bool TryParse(string[] args, out string? outputRoot, out string? scenarioId, out string? error)
    {
        var outputIndex = Array.FindIndex(args, arg => StringComparer.Ordinal.Equals(arg, OutputArgument));
        if (outputIndex < 0)
        {
            outputRoot = null;
            scenarioId = null;
            error = null;
            return false;
        }

        if (outputIndex + 1 >= args.Length || string.IsNullOrWhiteSpace(args[outputIndex + 1]))
        {
            outputRoot = null;
            scenarioId = null;
            error = $"{OutputArgument} requires an output directory.";
            return true;
        }

        outputRoot = Path.GetFullPath(args[outputIndex + 1]);
        var scenarioIndex = Array.FindIndex(args, arg => StringComparer.Ordinal.Equals(arg, ScenarioArgument));
        var scenarioCandidate = scenarioIndex >= 0 && scenarioIndex + 1 < args.Length
            ? args[scenarioIndex + 1]
            : null;
        scenarioId = scenarioCandidate;
        if (scenarioIndex >= 0 && string.IsNullOrWhiteSpace(scenarioCandidate))
        {
            error = $"{ScenarioArgument} requires a scenario id.";
            return true;
        }
        if (scenarioCandidate is not null &&
            !WholeWindowVisualEvidenceCatalog.All.Any(scenario => StringComparer.Ordinal.Equals(scenario.Id, scenarioCandidate)))
        {
            error = $"Unknown whole-window visual evidence scenario: {scenarioId}";
            return true;
        }

        error = null;
        return true;
    }

    internal static void Start(MainWindow anchor, string outputRoot, string? scenarioId)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            var exitCode = 1;
            try
            {
                exitCode = await CaptureAll(anchor, outputRoot, scenarioId);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
            }
            finally
            {
                if (Application.Current?.ApplicationLifetime is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                    desktop.Shutdown(exitCode);
            }
        }, DispatcherPriority.Background);
    }

    private static async Task<int> CaptureAll(MainWindow anchor, string outputRoot, string? scenarioId)
    {
        var hostDirectory = Path.Combine(outputRoot, "avalonia");
        var fullDirectory = Path.Combine(hostDirectory, "full");
        var clientDirectory = Path.Combine(hostDirectory, "client");
        Directory.CreateDirectory(fullDirectory);
        Directory.CreateDirectory(clientDirectory);
        var captures = new List<WholeWindowVisualEvidenceCapture>();
        var limitations = new List<string>();

        anchor.Width = WholeWindowVisualEvidenceCatalog.LogicalClientWidth;
        anchor.Height = WholeWindowVisualEvidenceCatalog.LogicalClientHeight;
        anchor.Position = new PixelPoint(40, 40);
        anchor.Show();

        var scenarios = scenarioId is null
            ? WholeWindowVisualEvidenceCatalog.All
            : [WholeWindowVisualEvidenceCatalog.Get(scenarioId)];
        foreach (var scenario in scenarios)
        {
            try
            {
                var fixture = DialogPaneVisualEvidenceFixtureFactory.Create();
                var assertions = anchor.PrepareWholeWindowVisualEvidence(scenario, fixture);
                await PumpLayout();
                anchor.Activate();
                await PumpLayout();
                anchor.NormalizeWholeWindowVisualEvidenceShellState(scenario);

                var fullPath = Path.Combine(fullDirectory, scenario.Id + ".png");
                var clientPath = Path.Combine(clientDirectory, scenario.Id + ".png");
                var fullRaster = Capture(anchor, fullPath);
                var clientRaster = Capture(
                    anchor,
                    clientPath,
                    WholeWindowVisualEvidenceCatalog.LogicalClientWidth,
                    WholeWindowVisualEvidenceCatalog.LogicalClientHeight);
                var semantic = anchor.CaptureWholeWindowVisualEvidenceSemanticState(scenario, assertions);
                captures.Add(new WholeWindowVisualEvidenceCapture(
                    scenario.Id,
                    "avalonia",
                    fullRaster.NonBackgroundPixelCount > 0 && clientRaster.NonBackgroundPixelCount > 0
                        ? "complete"
                        : "blocked",
                    $"avalonia/full/{scenario.Id}.png",
                    $"avalonia/client/{scenario.Id}.png",
                    clientRaster.LogicalWidth,
                    clientRaster.LogicalHeight,
                    clientRaster.PixelWidth,
                    clientRaster.PixelHeight,
                    96,
                    96,
                    96,
                    96,
                    clientRaster.NonBackgroundPixelCount,
                    Sha256(fullPath),
                    Sha256(clientPath),
                    semantic,
                    []));
            }
            catch (Exception ex)
            {
                limitations.Add($"{scenario.Id}: {ex.GetType().Name}: {ex.Message}");
                Console.Error.WriteLine($"Avalonia whole-window capture failed for {scenario.Id}: {ex}");
            }
        }

        anchor.Close();
        var manifest = new WholeWindowVisualEvidenceHostManifest(
            1,
            "avalonia",
            "visible-app-owned-full-client-render-target; native-non-client-excluded; scenario-isolated-process",
            WholeWindowVisualEvidenceCatalog.TargetDpi,
            WholeWindowVisualEvidenceCatalog.LogicalClientWidth,
            WholeWindowVisualEvidenceCatalog.LogicalClientHeight,
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            captures,
            limitations);
        File.WriteAllText(Path.Combine(hostDirectory, "manifest.json"), JsonSerializer.Serialize(manifest, JsonOptions));
        return captures.Count == scenarios.Count ? 0 : 1;
    }

    private static CaptureRaster Capture(
        Visual target,
        string path,
        double? requestedLogicalWidth = null,
        double? requestedLogicalHeight = null)
    {
        var logicalWidth = requestedLogicalWidth ?? (target is Window window ? window.ClientSize.Width : target.Bounds.Width);
        var logicalHeight = requestedLogicalHeight ?? (target is Window windowTarget ? windowTarget.ClientSize.Height : target.Bounds.Height);
        var width = Math.Max(1, (int)Math.Ceiling(logicalWidth));
        var height = Math.Max(1, (int)Math.Ceiling(logicalHeight));
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        bitmap.Render(target);
        bitmap.Save(path);

        var stride = width * 4;
        var byteCount = stride * height;
        var pointer = Marshal.AllocHGlobal(byteCount);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, width, height), pointer, byteCount, stride);
            var pixels = new byte[byteCount];
            Marshal.Copy(pointer, pixels, 0, byteCount);
            return new CaptureRaster(
                logicalWidth,
                logicalHeight,
                width,
                height,
                BgraRasterStatistics.CountNonBackgroundPixels(pixels));
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static async Task PumpLayout()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

    private sealed record CaptureRaster(
        double LogicalWidth,
        double LogicalHeight,
        int PixelWidth,
        int PixelHeight,
        long NonBackgroundPixelCount);
}
