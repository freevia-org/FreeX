using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Free.Shared.Drawing;
using FreeP.App.Avalonia;
using FreeP.App.Compositor;
using FreeP.VisualEvidence;
using Free.ToolsShared;

namespace FreeP.VisualEvidence.Avalonia;

internal static class AvaloniaWholeWindowVisualEvidenceCapture
{
    internal static bool TryParse(string[] args, out string? outputRoot, out string? scenarioId, out string? error)
    {
        var request = FreePVisualEvidenceCaptureOrchestration.ParseRequest(
            args,
            FreePVisualEvidenceRoutes.WholeWindow,
            WholeWindowVisualEvidenceCatalog.All.Select(scenario => scenario.Id));
        outputRoot = request.OutputRoot;
        scenarioId = request.ScenarioId;
        error = request.Error;
        return request.IsRequested;
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
        var outputPlan = FreePVisualEvidenceCaptureOrchestration.CreateHostOutputPlan(
            outputRoot,
            FreePVisualEvidenceCaptureOrchestration.AvaloniaHost,
            FreePVisualEvidenceRoutes.WholeWindow);

        anchor.Width = WholeWindowVisualEvidenceCatalog.LogicalClientWidth;
        anchor.Height = WholeWindowVisualEvidenceCatalog.LogicalClientHeight;
        anchor.Position = new PixelPoint(40, 40);
        anchor.Show();

        var run = await VisualEvidenceCaptureOrchestrator.RunScenariosAsync(
            WholeWindowVisualEvidenceCatalog.All,
            scenarioId,
            scenario => scenario.Id,
            outputPlan,
            logProgress: false,
            async scenario =>
            {
                var fixture = DialogPaneVisualEvidenceFixtureFactory.Create();
                var coordinator = new AvaloniaWholeWindowVisualEvidenceCoordinator(anchor.CreateVisualCaptureAdapter());
                var assertions = coordinator.Prepare(scenario, fixture);
                await PumpLayout();
                anchor.Activate();
                await PumpLayout();
                coordinator.Normalize(scenario);

                var scenarioOutput = FreePVisualEvidenceCaptureOrchestration.CreateScenarioOutputPlan(
                    outputRoot,
                    FreePVisualEvidenceCaptureOrchestration.AvaloniaHost,
                    scenario.Id,
                    FreePVisualEvidenceRoutes.WholeWindow);
                var fullPath = scenarioOutput.FullImagePath!;
                var clientPath = scenarioOutput.ClientImagePath!;
                var fullRaster = Capture(anchor, fullPath);
                var clientRaster = Capture(
                    anchor,
                    clientPath,
                    WholeWindowVisualEvidenceCatalog.LogicalClientWidth,
                    WholeWindowVisualEvidenceCatalog.LogicalClientHeight);
                var semantic = coordinator.CaptureSemantic(scenario, assertions);
                return new WholeWindowVisualEvidenceCapture(
                    scenario.Id,
                    "avalonia",
                    fullRaster.NonBackgroundPixelCount > 0 && clientRaster.NonBackgroundPixelCount > 0
                        ? "complete"
                        : "blocked",
                    scenarioOutput.FullImageRelativePath!,
                    scenarioOutput.ClientImageRelativePath!,
                    clientRaster.LogicalWidth,
                    clientRaster.LogicalHeight,
                    clientRaster.PixelWidth,
                    clientRaster.PixelHeight,
                    96,
                    96,
                    96,
                    96,
                    clientRaster.NonBackgroundPixelCount,
                    FreePVisualEvidenceCaptureOrchestration.Sha256(fullPath),
                    FreePVisualEvidenceCaptureOrchestration.Sha256(clientPath),
                    semantic,
                    []);
            },
            createBlockedCapture: (_, _) => null,
            createLimitation: (scenario, exception) =>
                $"{scenario.Id}: {exception.GetType().Name}: {exception.Message}",
            reportFailure: (scenario, exception) =>
                Console.Error.WriteLine($"Avalonia whole-window capture failed for {scenario.Id}: {exception}"));

        anchor.Close();
        return VisualEvidenceCaptureOrchestrator.FinalizeHostRun(
            outputPlan,
            run,
            (captures, limitations) => new WholeWindowVisualEvidenceHostManifest(
                1,
                "avalonia",
                "visible-app-owned-full-client-render-target; native-non-client-excluded; scenario-isolated-process",
                WholeWindowVisualEvidenceCatalog.TargetDpi,
                WholeWindowVisualEvidenceCatalog.LogicalClientWidth,
                WholeWindowVisualEvidenceCatalog.LogicalClientHeight,
                FreePVisualEvidenceCaptureOrchestration.UtcTimestamp(),
                captures,
                limitations),
            FreePVisualEvidenceCaptureOrchestration.HostManifestJsonOptions);
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
