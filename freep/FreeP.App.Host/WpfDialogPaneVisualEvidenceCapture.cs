using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Free.Shared.Drawing;
using Free.Shared.Theme;
using Free.Shared.Theme.Wpf;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

internal static class WpfDialogPaneVisualEvidenceCapture
{
    internal const string OutputArgument = "--dialog-pane-visual-evidence-output";
    internal const string ScenarioArgument = "--dialog-pane-visual-evidence-scenario";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    internal static bool TryRun(string[] args, out int exitCode)
    {
        var index = Array.FindIndex(args, arg => StringComparer.Ordinal.Equals(arg, OutputArgument));
        if (index < 0)
        {
            exitCode = 0;
            return false;
        }

        if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
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
            if (!DialogPaneVisualEvidenceCatalog.All.Any(scenario => StringComparer.Ordinal.Equals(scenario.Id, scenarioId)))
            {
                Console.Error.WriteLine($"Unknown visual evidence scenario: {scenarioId}");
                exitCode = 2;
                return true;
            }
        }

        exitCode = Run(Path.GetFullPath(args[index + 1]), scenarioId);
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
        Directory.CreateDirectory(hostDirectory);
        var captures = new List<DialogPaneVisualEvidenceCapture>();
        var progressPath = Path.Combine(hostDirectory, "capture-progress.log");
        File.WriteAllText(progressPath, string.Empty);
        var hostLimitations = new List<string>();

        var scenarios = scenarioId is null
            ? DialogPaneVisualEvidenceCatalog.All
            : [DialogPaneVisualEvidenceCatalog.Get(scenarioId)];

        foreach (var scenario in scenarios)
        {
            File.AppendAllText(progressPath, $"start {scenario.Id}{Environment.NewLine}");
            var fixture = DialogPaneVisualEvidenceFixtureFactory.Create();
            if (scenario.RouteId == "slideshow.custom-shows" && scenario.StateId != "populated")
                fixture.Presentation.CustomShows.Clear();

            var owner = new MainWindow
            {
                Width = DialogPaneVisualEvidenceCatalog.LogicalShellWidth,
                Height = DialogPaneVisualEvidenceCatalog.LogicalShellHeight,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 40,
                Top = 40,
            };
            Window? dialog = null;
            try
            {
                owner.Show();
                NormalizeOwnerContentSize(owner);
                owner.Activate();
                var assertions = owner.PrepareDialogPaneVisualEvidence(scenario, fixture).ToList();
                Window target = owner;

                if (scenario.SurfaceKind == DialogPaneVisualEvidenceSurfaceKind.Dialog)
                {
                    dialog = CreateDialog(owner, fixture, scenario, assertions);
                    dialog.Owner = owner;
                    dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                    dialog.Show();
                    target = dialog;
                }

                PumpLayout(target);
                PrepareLoadedDialogState(dialog, scenario, assertions);
                target.Activate();
                FocusFirstInputIfNeeded(target, scenario);
                PumpLayout(target);

                var fileName = scenario.Id + ".png";
                var imagePath = Path.Combine(hostDirectory, fileName);
                var captureRoot = scenario.SurfaceKind == DialogPaneVisualEvidenceSurfaceKind.Dialog
                    ? AppOwnedClientRoot(target)
                    : target.Content as FrameworkElement ?? target;
                var raster = Capture(captureRoot, imagePath);
                var metadataRoot = scenario.SurfaceKind == DialogPaneVisualEvidenceSurfaceKind.Dialog
                    ? target
                    : owner.DialogPaneVisualEvidenceMetadataRoot(scenario);
                var comparisonRoot = scenario.SurfaceKind == DialogPaneVisualEvidenceSurfaceKind.Dialog
                    ? captureRoot
                    : metadataRoot as FrameworkElement ?? captureRoot;
                var comparisonFileName = scenario.SurfaceKind == DialogPaneVisualEvidenceSurfaceKind.Dialog
                    ? fileName
                    : Path.Combine("targets", fileName);
                var comparisonPath = scenario.SurfaceKind == DialogPaneVisualEvidenceSurfaceKind.Dialog
                    ? imagePath
                    : Path.Combine(hostDirectory, comparisonFileName);
                if (scenario.SurfaceKind != DialogPaneVisualEvidenceSurfaceKind.Dialog)
                    Directory.CreateDirectory(Path.GetDirectoryName(comparisonPath)!);
                var pixelTarget = DialogPaneVisualEvidenceCatalog.PixelTargetFor(scenario);
                var comparisonRaster = ReferenceEquals(comparisonRoot, captureRoot)
                    ? raster
                    : Capture(comparisonRoot, comparisonPath, pixelTarget?.Width, pixelTarget?.Height);

                var focus = DescribeFocus(Keyboard.FocusedElement);
                var buttons = Buttons(metadataRoot);
                var controls = Controls(metadataRoot);
                assertions.AddRange(owner.CompleteDialogPaneVisualEvidence(scenario));
                captures.Add(new DialogPaneVisualEvidenceCapture(
                    scenario.Id,
                    scenario.RouteId,
                    scenario.StateId,
                    "wpf",
                    raster.NonBackgroundPixelCount > 0 ? "complete" : "blocked",
                    Path.Combine("wpf", fileName).Replace('\\', '/'),
                    raster.LogicalWidth,
                    raster.LogicalHeight,
                    raster.PixelWidth,
                    raster.PixelHeight,
                    raster.DpiX,
                    raster.DpiY,
                    raster.NonBackgroundPixelCount,
                    focus.Role,
                    focus.Label,
                    buttons,
                    controls,
                    assertions,
                    [],
                    raster.SourceDpiX,
                    raster.SourceDpiY,
                    "logical-96-dpi",
                    Path.Combine("wpf", comparisonFileName).Replace('\\', '/'),
                    comparisonRaster.LogicalWidth,
                    comparisonRaster.LogicalHeight));
                File.AppendAllText(progressPath, $"complete {scenario.Id}{Environment.NewLine}");
            }
            catch (Exception ex)
            {
                File.AppendAllText(progressPath, $"failed {scenario.Id}: {ex}{Environment.NewLine}");
                captures.Add(BlockedCapture(scenario, ex));
            }
            finally
            {
                dialog?.Close();
                owner.Close();
                PumpDispatcher();
            }
        }

        var manifest = new DialogPaneVisualEvidenceHostManifest(
            1,
            "wpf",
            "visible-app-owned-render-target",
            DialogPaneVisualEvidenceCatalog.TargetDpi,
            DialogPaneVisualEvidenceCatalog.LogicalShellWidth,
            DialogPaneVisualEvidenceCatalog.LogicalShellHeight,
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            captures,
            hostLimitations);
        File.WriteAllText(
            Path.Combine(hostDirectory, "manifest.json"),
            JsonSerializer.Serialize(manifest, JsonOptions));
        return captures.Count == scenarios.Count ? 0 : 1;
    }

    private static Window CreateDialog(
        MainWindow owner,
        DialogPaneVisualEvidenceFixture fixture,
        DialogPaneVisualEvidenceScenario scenario,
        List<DialogPaneVisualEvidenceAssertion> assertions)
    {
        switch (scenario.RouteId)
        {
            case "design.slide-size":
            {
                var dialog = new SlideSizeDialog(owner.Editor);
                if (scenario.StateId == "invalid")
                    dialog.SetInputForTests("0", "7.5", SlideSizeDialogUnit.Inches);
                return dialog;
            }
            case "insert.header-footer":
            {
                var focus = scenario.StateId == "date-time"
                    ? HeaderFooterCommandFocus.DateTime
                    : HeaderFooterCommandFocus.Footer;
                var dialog = new HeaderFooterDialog(owner.Editor, focus);
                dialog.PrepareForVisualEvidence(
                    showDateTime: true,
                    showFooter: scenario.StateId == "apply-to-all",
                    showSlideNumber: scenario.StateId == "apply-to-all",
                    footerText: scenario.StateId == "apply-to-all" ? "Confidential" : string.Empty);
                return dialog;
            }
            case "home.find-replace":
            {
                var dialog = new FindReplaceDialog(owner.Editor, scenario.StateId == "replace");
                dialog.SetInputForTests("revenue", scenario.StateId == "replace" ? "sales" : string.Empty, false, false);
                return dialog;
            }
            case "insert.hyperlink":
            {
                var current = scenario.StateId == "populated"
                    ? new Hyperlink { Url = "https://example.com/review", Tooltip = "Open review" }
                    : null;
                var dialog = new HyperlinkDialog(fixture.Presentation.Slides, current);
                if (scenario.StateId == "validation")
                {
                    var valid = dialog.ApplyForVisualEvidence(
                        HyperlinkDialogTargetKind.Url,
                        "not a url",
                        0,
                        string.Empty);
                    assertions.Add(new("validation-visible", !valid, "Invalid URL remains open with inline validation."));
                }
                return dialog;
            }
            case "chart.edit-data":
                return new ChartDataDialog(owner.Editor);
            case "slideshow.custom-shows":
            {
                var dialog = new CustomShowDialog(
                    new SlideShowCustomShowSession(() => owner.Editor));
                if (scenario.StateId == "validation")
                    dialog.PrepareValidationForVisualEvidence();
                return dialog;
            }
            default:
                throw new InvalidOperationException($"No WPF dialog capture adapter for {scenario.Id}.");
        }
    }

    private static void PrepareLoadedDialogState(
        Window? dialog,
        DialogPaneVisualEvidenceScenario scenario,
        List<DialogPaneVisualEvidenceAssertion> assertions)
    {
        if (dialog is SlideSizeDialog slideSize && scenario.RouteId == "design.slide-size" && scenario.StateId == "invalid")
        {
            var valid = slideSize.ApplyForTests();
            assertions.Add(new("validation-visible", !valid, slideSize.ValidationText));
            return;
        }

        if (dialog is null || scenario.RouteId != "chart.edit-data" || scenario.StateId != "validation")
            return;

        var prepared = ((ChartDataDialog)dialog).PrepareValidationForVisualEvidence();
        assertions.Add(new(
            "validation-visible",
            prepared,
            prepared
                ? $"Invalid chart value remains open with inline validation: {((ChartDataDialog)dialog).ValidationText}"
                : "The chart dialog could not enter and reject an invalid numeric cell."));
    }

    private static void FocusFirstInputIfNeeded(Window target, DialogPaneVisualEvidenceScenario scenario)
    {
        if (!scenario.CompareFocus)
            return;

        if (!string.IsNullOrWhiteSpace(DescribeFocus(Keyboard.FocusedElement).Role))
            return;

        Descendants(target).OfType<Control>()
            .FirstOrDefault(control => control is TextBox or ComboBox && control.IsEnabled && control.Focusable)
            ?.Focus();
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
        var desktopDpiX = 96d * scaleX;
        var desktopDpiY = 96d * scaleY;
        var logicalWidth = requestedLogicalWidth ?? target.ActualWidth;
        var logicalHeight = requestedLogicalHeight ?? target.ActualHeight;
        var nativeWidth = Math.Max(1, (int)Math.Ceiling(logicalWidth * scaleX));
        var nativeHeight = Math.Max(1, (int)Math.Ceiling(logicalHeight * scaleY));
        var nativeBitmap = new RenderTargetBitmap(nativeWidth, nativeHeight, desktopDpiX, desktopDpiY, PixelFormats.Pbgra32);
        var nativeVisual = new DrawingVisual();
        using (var nativeDrawing = nativeVisual.RenderOpen())
        {
            nativeDrawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, logicalWidth, logicalHeight));
            var brush = new VisualBrush(target)
            {
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,
                Stretch = Stretch.Fill,
            };
            nativeDrawing.DrawRectangle(brush, null, new Rect(0, 0, target.ActualWidth, target.ActualHeight));
        }
        nativeBitmap.Render(nativeVisual);

        var width = Math.Max(1, (int)Math.Ceiling(logicalWidth));
        var height = Math.Max(1, (int)Math.Ceiling(logicalHeight));
        var normalizedVisual = new DrawingVisual();
        using (var drawing = normalizedVisual.RenderOpen())
            drawing.DrawImage(nativeBitmap, new Rect(0, 0, logicalWidth, logicalHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(normalizedVisual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(path))
            encoder.Save(stream);

        var stride = width * 4;
        var pixels = new byte[stride * height];
        bitmap.CopyPixels(pixels, stride, 0);
        var nonBackground = BgraRasterStatistics.CountNonBackgroundPixels(pixels);
        return new CaptureRaster(
            logicalWidth,
            logicalHeight,
            width,
            height,
            96,
            96,
            nonBackground,
            desktopDpiX,
            desktopDpiY);
    }

    private static FrameworkElement AppOwnedClientRoot(Window window) =>
        VisualTreeHelper.GetChildrenCount(window) > 0 && VisualTreeHelper.GetChild(window, 0) is FrameworkElement client
            ? client
            : window.Content as FrameworkElement ?? window;

    private static IReadOnlyList<DialogPaneVisualEvidenceButton> Buttons(DependencyObject root) =>
        Descendants(root).OfType<Button>()
            .Where(button => button.TemplatedParent is null)
            .Select(ToButton)
            .Where(button => button is not null)
            .Cast<DialogPaneVisualEvidenceButton>()
            .ToArray();

    private static IReadOnlyList<DialogPaneVisualEvidenceControlState> Controls(DependencyObject root) =>
        Descendants(root).OfType<Control>()
            .Where(control => control.TemplatedParent is null)
            .Select(ToControlState)
            .Where(state => state is not null &&
                (state.Role is "button" or "checkbox" or "radio" || !string.IsNullOrWhiteSpace(state.Label)))
            .Cast<DialogPaneVisualEvidenceControlState>()
            .ToArray();

    private static DialogPaneVisualEvidenceControlState? ToControlState(Control control)
    {
        return control switch
        {
            Button button when ToButton(button) is { } action => new("button", action.ActionId, button.IsEnabled),
            CheckBox check => new("checkbox", NormalizeLabel(null, check.Content?.ToString()), check.IsEnabled, check.IsChecked),
            RadioButton radio => new("radio", NormalizeLabel(null, radio.Content?.ToString()), radio.IsEnabled, radio.IsChecked),
            ComboBox combo => new("combobox", NormalizeLabel(AutomationProperties.GetName(combo)), combo.IsEnabled, null, combo.SelectedIndex >= 0),
            TextBox box => new("textbox", NormalizeLabel(AutomationProperties.GetName(box)), box.IsEnabled),
            _ => null,
        };
    }

    private static (string Role, string Label) DescribeFocus(IInputElement? focused) => focused switch
    {
        Button button => ("button", NormalizeLabel(AutomationProperties.GetName(button), button.Content?.ToString())),
        CheckBox check => ("checkbox", NormalizeLabel(null, check.Content?.ToString())),
        RadioButton radio => ("radio", NormalizeLabel(null, radio.Content?.ToString())),
        ComboBox combo => ("combobox", NormalizeLabel(AutomationProperties.GetName(combo))),
        TextBox box => ("textbox", NormalizeLabel(AutomationProperties.GetName(box))),
        _ => (string.Empty, string.Empty),
    };

    private static DialogPaneVisualEvidenceButton? ToButton(Button button)
    {
        var fallback = button.Content as string;
        var label = NormalizeLabel(AutomationProperties.GetName(button), fallback);
        var automationId = NormalizeLabel(AutomationProperties.GetAutomationId(button));
        var actionId = string.IsNullOrWhiteSpace(automationId) ? SemanticActionId(label) : automationId;
        return string.IsNullOrWhiteSpace(actionId)
            ? null
            : new(actionId, label, button.IsEnabled, button.IsDefault, button.IsCancel);
    }

    private static string SemanticActionId(string label)
    {
        var value = label.Trim().ToLowerInvariant();
        if (value.StartsWith("+", StringComparison.Ordinal))
            value = "add " + value[1..];
        else if (value.StartsWith("-", StringComparison.Ordinal))
            value = "remove " + value[1..];
        var chars = value.Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray();
        return string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string NormalizeLabel(string? label, string? fallback = null) =>
        (string.IsNullOrWhiteSpace(label) ? fallback ?? string.Empty : label).Trim().TrimEnd(':').Replace("_", string.Empty);

    private static void NormalizeOwnerContentSize(Window owner)
    {
        owner.WindowState = WindowState.Normal;
        owner.UpdateLayout();
        if (owner.Content is not FrameworkElement content)
            return;
        owner.Width += DialogPaneVisualEvidenceCatalog.LogicalShellWidth - content.ActualWidth;
        owner.Height += DialogPaneVisualEvidenceCatalog.LogicalShellHeight - content.ActualHeight;
        owner.UpdateLayout();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
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

    private static DialogPaneVisualEvidenceCapture BlockedCapture(
        DialogPaneVisualEvidenceScenario scenario,
        Exception exception) =>
        new(
            scenario.Id,
            scenario.RouteId,
            scenario.StateId,
            "wpf",
            "blocked",
            string.Empty,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            string.Empty,
            string.Empty,
            [],
            [],
            [new("capture-completed", false, exception.Message)],
            [$"Capture failed: {exception.GetType().Name}: {exception.Message}"]);
}
