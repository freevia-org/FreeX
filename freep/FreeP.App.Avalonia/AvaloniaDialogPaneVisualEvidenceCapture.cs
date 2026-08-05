using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Free.Shared.Drawing;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal static class AvaloniaDialogPaneVisualEvidenceCapture
{
    internal const string OutputArgument = "--dialog-pane-visual-evidence-output";
    internal const string ScenarioArgument = "--dialog-pane-visual-evidence-scenario";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    internal static bool TryParse(string[] args, out string? outputRoot, out string? scenarioId, out string? error)
    {
        var index = Array.FindIndex(args, arg => StringComparer.Ordinal.Equals(arg, OutputArgument));
        if (index < 0)
        {
            outputRoot = null;
            scenarioId = null;
            error = null;
            return false;
        }

        if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
        {
            outputRoot = null;
            scenarioId = null;
            error = $"{OutputArgument} requires an output directory.";
            return true;
        }

        outputRoot = Path.GetFullPath(args[index + 1]);
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
            !DialogPaneVisualEvidenceCatalog.All.Any(scenario => StringComparer.Ordinal.Equals(scenario.Id, scenarioCandidate)))
        {
            error = $"Unknown visual evidence scenario: {scenarioCandidate}";
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
        Directory.CreateDirectory(hostDirectory);
        var progressPath = Path.Combine(hostDirectory, "capture-progress.log");
        File.WriteAllText(progressPath, string.Empty);
        var captures = new List<DialogPaneVisualEvidenceCapture>();
        var hostLimitations = new List<string>();

        anchor.Width = DialogPaneVisualEvidenceCatalog.LogicalShellWidth;
        anchor.Height = DialogPaneVisualEvidenceCatalog.LogicalShellHeight;
        anchor.Position = new PixelPoint(40, 40);
        anchor.Show();

        var scenarios = scenarioId is null
            ? DialogPaneVisualEvidenceCatalog.All
            : [DialogPaneVisualEvidenceCatalog.Get(scenarioId)];

        foreach (var scenario in scenarios)
        {
            File.AppendAllText(progressPath, $"start {scenario.Id}{Environment.NewLine}");
            Window? dialog = null;
            try
            {
                var fixture = DialogPaneVisualEvidenceFixtureFactory.Create();
                if (scenario.RouteId == "slideshow.custom-shows" && scenario.StateId != "populated")
                    fixture.Presentation.CustomShows.Clear();

                var assertions = anchor.PrepareDialogPaneVisualEvidence(scenario, fixture).ToList();
                Window target = anchor;
                if (scenario.SurfaceKind == DialogPaneVisualEvidenceSurfaceKind.Dialog)
                {
                    dialog = CreateDialog(anchor, fixture, scenario, assertions);
                    dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                    dialog.Show(anchor);
                    target = dialog;
                }

                await PumpLayout();
                PrepareLoadedDialogState(dialog, scenario, assertions);
                target.Activate();
                FocusFirstInputIfNeeded(target, scenario);
                await PumpLayout();

                var metadataRoot = scenario.SurfaceKind == DialogPaneVisualEvidenceSurfaceKind.Dialog
                    ? target
                    : anchor.DialogPaneVisualEvidenceMetadataRoot(scenario);
                if (scenario.RouteId == "review.comments-pane")
                {
                    Descendants(metadataRoot).OfType<ScrollViewer>().FirstOrDefault()?.SetCurrentValue(
                        ScrollViewer.OffsetProperty,
                        default(Vector));
                    await PumpLayout();
                }

                var fileName = scenario.Id + ".png";
                var imagePath = Path.Combine(hostDirectory, fileName);
                var raster = Capture(target, imagePath);
                var focus = DescribeFocus(target.FocusManager?.GetFocusedElement());
                var comparisonFileName = scenario.SurfaceKind == DialogPaneVisualEvidenceSurfaceKind.Dialog
                    ? fileName
                    : Path.Combine("targets", fileName);
                var comparisonPath = scenario.SurfaceKind == DialogPaneVisualEvidenceSurfaceKind.Dialog
                    ? imagePath
                    : Path.Combine(hostDirectory, comparisonFileName);
                if (scenario.SurfaceKind != DialogPaneVisualEvidenceSurfaceKind.Dialog)
                    Directory.CreateDirectory(Path.GetDirectoryName(comparisonPath)!);
                var pixelTarget = DialogPaneVisualEvidenceCatalog.PixelTargetFor(scenario);
                var comparisonRaster = ReferenceEquals(metadataRoot, target)
                    ? raster
                    : Capture(metadataRoot, comparisonPath, pixelTarget?.Width, pixelTarget?.Height);
                var buttons = Buttons(metadataRoot);
                var controls = Controls(metadataRoot);
                assertions.AddRange(anchor.CompleteDialogPaneVisualEvidence(scenario));

                captures.Add(new DialogPaneVisualEvidenceCapture(
                    scenario.Id,
                    scenario.RouteId,
                    scenario.StateId,
                    "avalonia",
                    raster.NonBackgroundPixelCount > 0 ? "complete" : "blocked",
                    Path.Combine("avalonia", fileName).Replace('\\', '/'),
                    target.ClientSize.Width,
                    target.ClientSize.Height,
                    raster.PixelWidth,
                    raster.PixelHeight,
                    96,
                    96,
                    raster.NonBackgroundPixelCount,
                    focus.Role,
                    focus.Label,
                    buttons,
                    controls,
                    assertions,
                    [],
                    96,
                    96,
                    "logical-96-dpi",
                    Path.Combine("avalonia", comparisonFileName).Replace('\\', '/'),
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
                await PumpLayout();
            }
        }

        anchor.Close();
        var manifest = new DialogPaneVisualEvidenceHostManifest(
            1,
            "avalonia",
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
                var dialog = new CustomShowDialog(owner);
                if (scenario.StateId == "validation")
                    dialog.PrepareValidationForVisualEvidence();
                return dialog;
            }
            default:
                throw new InvalidOperationException($"No Avalonia dialog capture adapter for {scenario.Id}.");
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
        if (!string.IsNullOrWhiteSpace(DescribeFocus(target.FocusManager?.GetFocusedElement()).Role))
            return;
        Descendants(target).OfType<Control>()
            .FirstOrDefault(control => control is TextBox or ComboBox && control.IsEnabled && control.Focusable)
            ?.Focus();
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

    private static IReadOnlyList<DialogPaneVisualEvidenceButton> Buttons(Visual root) =>
        Descendants(root).OfType<Button>()
            .Where(button => button is not ToggleButton && button.TemplatedParent is null)
            .Select(ToButton)
            .Where(button => button is not null)
            .Cast<DialogPaneVisualEvidenceButton>()
            .ToArray();

    private static IReadOnlyList<DialogPaneVisualEvidenceControlState> Controls(Visual root) =>
        Descendants(root).OfType<Control>()
            .Where(control => control.TemplatedParent is null)
            .Select(ToControlState)
            .Where(state => state is not null &&
                (state.Role is "button" or "checkbox" or "radio" || !string.IsNullOrWhiteSpace(state.Label)))
            .Cast<DialogPaneVisualEvidenceControlState>()
            .ToArray();

    private static DialogPaneVisualEvidenceControlState? ToControlState(Control control) => control switch
    {
        CheckBox check => new("checkbox", NormalizeLabel(null, check.Content?.ToString()), check.IsEnabled, check.IsChecked),
        RadioButton radio => new("radio", NormalizeLabel(null, radio.Content?.ToString()), radio.IsEnabled, radio.IsChecked),
        Button button when button is not ToggleButton && ToButton(button) is { } action => new("button", action.ActionId, button.IsEnabled),
        ComboBox combo => new("combobox", NormalizeLabel(AutomationProperties.GetName(combo)), combo.IsEnabled, null, combo.SelectedIndex >= 0),
        TextBox box => new("textbox", NormalizeLabel(AutomationProperties.GetName(box)), box.IsEnabled),
        _ => null,
    };

    private static (string Role, string Label) DescribeFocus(IInputElement? focused) => focused switch
    {
        CheckBox check => ("checkbox", NormalizeLabel(null, check.Content?.ToString())),
        RadioButton radio => ("radio", NormalizeLabel(null, radio.Content?.ToString())),
        Button button => ("button", NormalizeLabel(AutomationProperties.GetName(button), button.Content?.ToString())),
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

    private static IEnumerable<Visual> Descendants(Visual root)
    {
        yield return root;
        foreach (var descendant in root.GetVisualDescendants())
            yield return descendant;
    }

    private static async Task PumpLayout()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

    private static DialogPaneVisualEvidenceCapture BlockedCapture(
        DialogPaneVisualEvidenceScenario scenario,
        Exception exception) =>
        new(
            scenario.Id,
            scenario.RouteId,
            scenario.StateId,
            "avalonia",
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

    private sealed record CaptureRaster(
        double LogicalWidth,
        double LogicalHeight,
        int PixelWidth,
        int PixelHeight,
        long NonBackgroundPixelCount);
}
