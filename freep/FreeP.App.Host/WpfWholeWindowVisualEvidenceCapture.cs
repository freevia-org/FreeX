using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Free.Shared.Drawing;
using Free.Shared.Theme;
using Free.Shared.Theme.Wpf;
using FreeP.App.Compositor;

namespace FreeP.App.Host;

internal static class WpfWholeWindowVisualEvidenceCapture
{
    internal const string OutputArgument = "--whole-window-visual-evidence-output";
    internal const string ScenarioArgument = "--whole-window-visual-evidence-scenario";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    internal static bool TryRun(string[] args, out int exitCode)
    {
        var outputIndex = Array.FindIndex(args, arg => StringComparer.Ordinal.Equals(arg, OutputArgument));
        if (outputIndex < 0)
        {
            exitCode = 0;
            return false;
        }

        if (outputIndex + 1 >= args.Length || string.IsNullOrWhiteSpace(args[outputIndex + 1]))
        {
            Console.Error.WriteLine($"{OutputArgument} requires an output directory.");
            exitCode = 2;
            return true;
        }

        var scenarioIndex = Array.FindIndex(args, arg => StringComparer.Ordinal.Equals(arg, ScenarioArgument));
        string? scenarioId = null;
        if (scenarioIndex >= 0)
        {
            if (scenarioIndex + 1 >= args.Length || string.IsNullOrWhiteSpace(args[scenarioIndex + 1]))
            {
                Console.Error.WriteLine($"{ScenarioArgument} requires a scenario id.");
                exitCode = 2;
                return true;
            }

            scenarioId = args[scenarioIndex + 1];
            if (!WholeWindowVisualEvidenceCatalog.All.Any(scenario => StringComparer.Ordinal.Equals(scenario.Id, scenarioId)))
            {
                Console.Error.WriteLine($"Unknown whole-window visual evidence scenario: {scenarioId}");
                exitCode = 2;
                return true;
            }
        }

        exitCode = Run(Path.GetFullPath(args[outputIndex + 1]), scenarioId);
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
        var hostDirectory = Path.Combine(outputRoot, "wpf");
        var fullDirectory = Path.Combine(hostDirectory, "full");
        var clientDirectory = Path.Combine(hostDirectory, "client");
        Directory.CreateDirectory(fullDirectory);
        Directory.CreateDirectory(clientDirectory);
        var captures = new List<WholeWindowVisualEvidenceCapture>();
        var limitations = new List<string>();
        var scenarios = scenarioId is null
            ? WholeWindowVisualEvidenceCatalog.All
            : [WholeWindowVisualEvidenceCatalog.Get(scenarioId)];

        foreach (var scenario in scenarios)
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
                var assertions = owner.PrepareWholeWindowVisualEvidence(scenario, fixture);
                PumpLayout(owner);
                owner.Activate();
                PumpLayout(owner);
                owner.NormalizeWholeWindowVisualEvidenceShellState(scenario);
                owner.UpdateLayout();

                var root = owner.Content as FrameworkElement
                    ?? throw new InvalidOperationException("The WPF whole-window capture has no app-owned client root.");
                var fullPath = Path.Combine(fullDirectory, scenario.Id + ".png");
                var clientPath = Path.Combine(clientDirectory, scenario.Id + ".png");
                var fullRaster = Capture(root, fullPath);
                var clientRaster = Capture(
                    root,
                    clientPath,
                    WholeWindowVisualEvidenceCatalog.LogicalClientWidth,
                    WholeWindowVisualEvidenceCatalog.LogicalClientHeight);
                var semantic = owner.CaptureWholeWindowVisualEvidenceSemanticState(scenario, assertions);
                captures.Add(new WholeWindowVisualEvidenceCapture(
                    scenario.Id,
                    "wpf",
                    fullRaster.NonBackgroundPixelCount > 0 && clientRaster.NonBackgroundPixelCount > 0
                        ? "complete"
                        : "blocked",
                    $"wpf/full/{scenario.Id}.png",
                    $"wpf/client/{scenario.Id}.png",
                    clientRaster.LogicalWidth,
                    clientRaster.LogicalHeight,
                    clientRaster.PixelWidth,
                    clientRaster.PixelHeight,
                    clientRaster.DpiX,
                    clientRaster.DpiY,
                    clientRaster.SourceDpiX,
                    clientRaster.SourceDpiY,
                    clientRaster.NonBackgroundPixelCount,
                    Sha256(fullPath),
                    Sha256(clientPath),
                    semantic,
                    []));
            }
            catch (Exception ex)
            {
                limitations.Add($"{scenario.Id}: {ex.GetType().Name}: {ex.Message}");
                Console.Error.WriteLine($"WPF whole-window capture failed for {scenario.Id}: {ex}");
            }
            finally
            {
                owner.Close();
                PumpDispatcher();
            }
        }

        var manifest = new WholeWindowVisualEvidenceHostManifest(
            1,
            "wpf",
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

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
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
