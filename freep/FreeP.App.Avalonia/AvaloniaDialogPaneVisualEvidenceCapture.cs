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
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal static class AvaloniaDialogPaneVisualEvidenceCapture
{
    internal const string OutputArgument = "--dialog-pane-visual-evidence-output";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    internal static bool TryParse(string[] args, out string? outputRoot, out string? error)
    {
        var index = Array.FindIndex(args, arg => StringComparer.Ordinal.Equals(arg, OutputArgument));
        if (index < 0)
        {
            outputRoot = null;
            error = null;
            return false;
        }

        if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
        {
            outputRoot = null;
            error = $"{OutputArgument} requires an output directory.";
            return true;
        }

        outputRoot = Path.GetFullPath(args[index + 1]);
        error = null;
        return true;
    }

    internal static void Start(MainWindow anchor, string outputRoot)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            var exitCode = 1;
            try
            {
                exitCode = await CaptureAll(anchor, outputRoot);
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

    private static async Task<int> CaptureAll(MainWindow anchor, string outputRoot)
    {
        var hostDirectory = Path.Combine(outputRoot, "avalonia");
        Directory.CreateDirectory(hostDirectory);
        var progressPath = Path.Combine(hostDirectory, "capture-progress.log");
        File.WriteAllText(progressPath, string.Empty);
        var captures = new List<DialogPaneVisualEvidenceCapture>();
        var hostLimitations = new List<string>
        {
            "Visible Avalonia windows are captured from their app-owned render targets; native non-client title-bar pixels are excluded.",
        };

        anchor.Width = DialogPaneVisualEvidenceCatalog.LogicalShellWidth;
        anchor.Height = DialogPaneVisualEvidenceCatalog.LogicalShellHeight;
        anchor.Position = new PixelPoint(40, 40);
        anchor.Show();

        foreach (var scenario in DialogPaneVisualEvidenceCatalog.All)
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

                var fileName = scenario.Id + ".png";
                var imagePath = Path.Combine(hostDirectory, fileName);
                var raster = Capture(target, imagePath);
                assertions.AddRange(anchor.CompleteDialogPaneVisualEvidence(scenario));
                var focus = DescribeFocus(target.FocusManager?.GetFocusedElement());

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
                    Buttons(target),
                    Controls(target),
                    assertions,
                    []));
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
        return captures.Count == DialogPaneVisualEvidenceCatalog.All.Count ? 0 : 1;
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
                {
                    dialog.SetInputForTests("0", "7.5", SlideSizeDialogUnit.Inches);
                    assertions.Add(new("validation-visible", !dialog.ApplyForTests(), dialog.ValidationText));
                }
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
        if (dialog is null || scenario.RouteId != "chart.edit-data" || scenario.StateId != "validation")
            return;

        var numericBox = Descendants(dialog).OfType<TextBox>()
            .FirstOrDefault(box => double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out _));
        if (numericBox is not null)
        {
            numericBox.Text = "not-a-number";
            numericBox.Focus();
        }
        assertions.Add(new(
            "validation-input-prepared",
            numericBox is not null,
            numericBox is null ? "No realized numeric chart cell was available." : "A realized chart value cell contains invalid numeric input."));
    }

    private static void FocusFirstInputIfNeeded(Window target, DialogPaneVisualEvidenceScenario scenario)
    {
        if (!scenario.CompareFocus || target.FocusManager?.GetFocusedElement() is not null)
            return;
        Descendants(target).OfType<Control>()
            .FirstOrDefault(control => control is TextBox or ComboBox && control.IsEnabled && control.Focusable)
            ?.Focus();
    }

    private static CaptureRaster Capture(Window target, string path)
    {
        var width = Math.Max(1, (int)Math.Ceiling(target.ClientSize.Width));
        var height = Math.Max(1, (int)Math.Ceiling(target.ClientSize.Height));
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
            return new CaptureRaster(width, height, CountNonBackgroundPixels(pixels));
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    private static long CountNonBackgroundPixels(byte[] pixels)
    {
        if (pixels.Length < 4)
            return 0;
        var b = pixels[0];
        var g = pixels[1];
        var r = pixels[2];
        long count = 0;
        for (var index = 0; index < pixels.Length; index += 4)
        {
            if (Math.Abs(pixels[index] - b) + Math.Abs(pixels[index + 1] - g) + Math.Abs(pixels[index + 2] - r) > 12)
                count++;
        }
        return count;
    }

    private static IReadOnlyList<DialogPaneVisualEvidenceButton> Buttons(Visual root) =>
        Descendants(root).OfType<Button>()
            .Select(button => new DialogPaneVisualEvidenceButton(
                NormalizeLabel(button.Content?.ToString()),
                button.IsEnabled,
                button.IsDefault,
                button.IsCancel))
            .Where(button => !string.IsNullOrWhiteSpace(button.Label))
            .ToArray();

    private static IReadOnlyList<DialogPaneVisualEvidenceControlState> Controls(Visual root) =>
        Descendants(root).OfType<Control>()
            .Select(ToControlState)
            .Where(state => state is not null)
            .Cast<DialogPaneVisualEvidenceControlState>()
            .ToArray();

    private static DialogPaneVisualEvidenceControlState? ToControlState(Control control) => control switch
    {
        CheckBox check => new("checkbox", NormalizeLabel(check.Content?.ToString()), check.IsEnabled, check.IsChecked),
        RadioButton radio => new("radio", NormalizeLabel(radio.Content?.ToString()), radio.IsEnabled, radio.IsChecked),
        Button button => new("button", NormalizeLabel(button.Content?.ToString()), button.IsEnabled),
        ComboBox combo => new("combobox", NormalizeLabel(AutomationProperties.GetName(combo)), combo.IsEnabled, null, combo.SelectedIndex >= 0),
        TextBox box => new("textbox", NormalizeLabel(AutomationProperties.GetName(box)), box.IsEnabled),
        _ => null,
    };

    private static (string Role, string Label) DescribeFocus(IInputElement? focused) => focused switch
    {
        CheckBox check => ("checkbox", NormalizeLabel(check.Content?.ToString())),
        RadioButton radio => ("radio", NormalizeLabel(radio.Content?.ToString())),
        Button button => ("button", NormalizeLabel(button.Content?.ToString())),
        ComboBox combo => ("combobox", NormalizeLabel(AutomationProperties.GetName(combo))),
        TextBox box => ("textbox", NormalizeLabel(AutomationProperties.GetName(box))),
        Control control => (control.GetType().Name.ToLowerInvariant(), NormalizeLabel(AutomationProperties.GetName(control))),
        _ => (string.Empty, string.Empty),
    };

    private static string NormalizeLabel(string? label) =>
        (label ?? string.Empty).Trim().TrimEnd(':');

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

    private sealed record CaptureRaster(int PixelWidth, int PixelHeight, long NonBackgroundPixelCount);
}
