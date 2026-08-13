using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Free.Shared.Drawing;
using Free.Shared.Theme;
using Free.Shared.Theme.Wpf;
using FreeP.App.Compositor;
using FreeP.App.Host;
using FreeP.VisualEvidence;
using Free.ToolsShared;

namespace FreeP.VisualEvidence.Wpf;

internal static class WpfWholeWindowVisualEvidenceCapture
{
    internal static bool TryRun(string[] args, out int exitCode)
    {
        var request = FreePVisualEvidenceCaptureOrchestration.ParseRequest(
            args,
            FreePVisualEvidenceRoutes.WholeWindow,
            WholeWindowVisualEvidenceCatalog.All.Select(scenario => scenario.Id));
        if (!request.IsRequested)
        {
            exitCode = 0;
            return false;
        }

        if (!request.IsValid)
        {
            Console.Error.WriteLine(request.Error);
            exitCode = 2;
            return true;
        }

        exitCode = Run(request.OutputRoot!, request.ScenarioId);
        return true;
    }

    private static int Run(string outputRoot, string? scenarioId)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        AppComposition.InstallSharedSeams();
        WpfThemeApplier.Apply(app, BrandThemes.FreeP, "FreeP");
        var result = 1;
        app.Startup += (_, _) => app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            try
            {
                result = CaptureAll(outputRoot, scenarioId);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                result = 1;
            }
            finally
            {
                app.Shutdown(result);
            }
        }));
        app.Run();
        return result;
    }

    private static int CaptureAll(string outputRoot, string? scenarioId)
    {
        var outputPlan = FreePVisualEvidenceCaptureOrchestration.CreateHostOutputPlan(
            outputRoot,
            FreePVisualEvidenceCaptureOrchestration.WpfHost,
            FreePVisualEvidenceRoutes.WholeWindow);
        var run = VisualEvidenceCaptureOrchestrator.RunScenariosAsync(
            WholeWindowVisualEvidenceCatalog.All,
            scenarioId,
            scenario => scenario.Id,
            outputPlan,
            logProgress: false,
            scenario =>
            {
                var fixture = DialogPaneVisualEvidenceFixtureFactory.Create();
                var owner = new MainWindow
                {
                    Width = WholeWindowVisualEvidenceCatalog.LogicalClientWidth,
                    Height = WholeWindowVisualEvidenceCatalog.LogicalClientHeight,
                    WindowState = WindowState.Normal,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = 40,
                    Top = 40,
                };
                try
                {
                    owner.Show();
                    NormalizeContentSize(owner);
                    owner.Activate();
                    var coordinator = new WpfWholeWindowVisualEvidenceCoordinator(owner.CreateVisualCaptureAdapter());
                    var assertions = coordinator.Prepare(scenario, fixture);
                    PumpLayout(owner);
                    owner.Activate();
                    PumpLayout(owner);
                    coordinator.Normalize(scenario);
                    owner.UpdateLayout();

                    var root = owner.Content as FrameworkElement
                        ?? throw new InvalidOperationException("The WPF whole-window capture has no app-owned client root.");
                    var scenarioOutput = FreePVisualEvidenceCaptureOrchestration.CreateScenarioOutputPlan(
                        outputRoot,
                        FreePVisualEvidenceCaptureOrchestration.WpfHost,
                        scenario.Id,
                        FreePVisualEvidenceRoutes.WholeWindow);
                    var fullPath = scenarioOutput.FullImagePath!;
                    var clientPath = scenarioOutput.ClientImagePath!;
                    var fullRaster = Capture(root, fullPath);
                    var clientRaster = Capture(
                        root,
                        clientPath,
                        WholeWindowVisualEvidenceCatalog.LogicalClientWidth,
                        WholeWindowVisualEvidenceCatalog.LogicalClientHeight);
                    var semantic = coordinator.CaptureSemantic(scenario, assertions);
                    return Task.FromResult(new WholeWindowVisualEvidenceCapture(
                        scenario.Id,
                        "wpf",
                        fullRaster.NonBackgroundPixelCount > 0 && clientRaster.NonBackgroundPixelCount > 0
                            ? "complete"
                            : "blocked",
                        scenarioOutput.FullImageRelativePath!,
                        scenarioOutput.ClientImageRelativePath!,
                        clientRaster.LogicalWidth,
                        clientRaster.LogicalHeight,
                        clientRaster.PixelWidth,
                        clientRaster.PixelHeight,
                        clientRaster.DpiX,
                        clientRaster.DpiY,
                        clientRaster.SourceDpiX,
                        clientRaster.SourceDpiY,
                        clientRaster.NonBackgroundPixelCount,
                        FreePVisualEvidenceCaptureOrchestration.Sha256(fullPath),
                        FreePVisualEvidenceCaptureOrchestration.Sha256(clientPath),
                        semantic,
                        []));
                }
                finally
                {
                    owner.Close();
                    PumpDispatcher();
                }
            },
            createBlockedCapture: (_, _) => null,
            createLimitation: (scenario, exception) =>
                $"{scenario.Id}: {exception.GetType().Name}: {exception.Message}",
            reportFailure: (scenario, exception) =>
                Console.Error.WriteLine($"WPF whole-window capture failed for {scenario.Id}: {exception}"))
            .GetAwaiter()
            .GetResult();

        return VisualEvidenceCaptureOrchestrator.FinalizeHostRun(
            outputPlan,
            run,
            (captures, limitations) => new WholeWindowVisualEvidenceHostManifest(
                1,
                "wpf",
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
        FrameworkElement target,
        string path,
        double? requestedLogicalWidth = null,
        double? requestedLogicalHeight = null)
    {
        var source = PresentationSource.FromVisual(target);
        var scaleX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1d;
        var scaleY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1d;
        var sourceDpiX = 96d * scaleX;
        var sourceDpiY = 96d * scaleY;
        var logicalWidth = requestedLogicalWidth ?? target.ActualWidth;
        var logicalHeight = requestedLogicalHeight ?? target.ActualHeight;
        var width = Math.Max(1, (int)Math.Ceiling(logicalWidth));
        var height = Math.Max(1, (int)Math.Ceiling(logicalHeight));
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, logicalWidth, logicalHeight));
            drawing.DrawRectangle(
                new VisualBrush(target)
                {
                    AlignmentX = AlignmentX.Left,
                    AlignmentY = AlignmentY.Top,
                    Stretch = Stretch.Fill,
                },
                null,
                new Rect(0, 0, logicalWidth, logicalHeight));
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(path))
            encoder.Save(stream);

        var stride = width * 4;
        var pixels = new byte[stride * height];
        bitmap.CopyPixels(pixels, stride, 0);
        return new CaptureRaster(
            logicalWidth,
            logicalHeight,
            width,
            height,
            96,
            96,
            BgraRasterStatistics.CountNonBackgroundPixels(pixels),
            sourceDpiX,
            sourceDpiY);
    }

    private static void NormalizeContentSize(Window owner)
    {
        owner.UpdateLayout();
        if (owner.Content is not FrameworkElement content)
            return;
        owner.Width += WholeWindowVisualEvidenceCatalog.LogicalClientWidth - content.ActualWidth;
        owner.Height += WholeWindowVisualEvidenceCatalog.LogicalClientHeight - content.ActualHeight;
        owner.UpdateLayout();
    }

    private static void PumpLayout(Window window)
    {
        window.UpdateLayout();
        PumpDispatcher();
        window.UpdateLayout();
    }

    private static void PumpDispatcher() =>
        Application.Current.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));

    private sealed record CaptureRaster(
        double LogicalWidth,
        double LogicalHeight,
        int PixelWidth,
        int PixelHeight,
        double DpiX,
        double DpiY,
        long NonBackgroundPixelCount,
        double SourceDpiX,
        double SourceDpiY);
}
