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
using Free.Shared.Theme;
using Free.Shared.Theme.Wpf;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

internal static class WpfDialogPaneVisualEvidenceCapture
{
    internal const string OutputArgument = "--dialog-pane-visual-evidence-output";

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

        exitCode = Run(Path.GetFullPath(args[index + 1]));
        return true;
    }

    private static int Run(string outputRoot)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        AppComposition.InstallSharedSeams();
        WpfThemeApplier.Apply(app, BrandThemes.FreeP, "FreeP");

        var result = 1;
        app.Startup += (_, _) => app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            try
            {
                result = CaptureAll(outputRoot);
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

    private static int CaptureAll(string outputRoot)
    {
        var hostDirectory = Path.Combine(outputRoot, "wpf");
        Directory.CreateDirectory(hostDirectory);
        var captures = new List<DialogPaneVisualEvidenceCapture>();
        var progressPath = Path.Combine(hostDirectory, "capture-progress.log");
        File.WriteAllText(progressPath, string.Empty);
        var hostLimitations = new List<string>
        {
            "Visible WPF windows are captured from their app-owned render targets; native non-client title-bar pixels are excluded.",
        };

        foreach (var scenario in DialogPaneVisualEvidenceCatalog.All)
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
                var raster = Capture(target, imagePath);
                assertions.AddRange(owner.CompleteDialogPaneVisualEvidence(scenario));

                var focus = DescribeFocus(Keyboard.FocusedElement);
                captures.Add(new DialogPaneVisualEvidenceCapture(
                    scenario.Id,
                    scenario.RouteId,
                    scenario.StateId,
                    "wpf",
                    raster.NonBackgroundPixelCount > 0 ? "complete" : "blocked",
                    Path.Combine("wpf", fileName).Replace('\\', '/'),
                    target.ActualWidth,
                    target.ActualHeight,
                    raster.PixelWidth,
                    raster.PixelHeight,
                    raster.DpiX,
                    raster.DpiY,
                    raster.NonBackgroundPixelCount,
                    focus.Role,
                    focus.Label,
                    Buttons(target),
                    Controls(target),
                    assertions,
                    raster.Limitations));
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
                throw new InvalidOperationException($"No WPF dialog capture adapter for {scenario.Id}.");
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
        if (!scenario.CompareFocus || Keyboard.FocusedElement is not null)
            return;

        Descendants(target).OfType<Control>()
            .FirstOrDefault(control => control is TextBox or ComboBox && control.IsEnabled && control.Focusable)
            ?.Focus();
    }

    private static CaptureRaster Capture(Window target, string path)
    {
        var source = PresentationSource.FromVisual(target);
        var scaleX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1d;
        var scaleY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1d;
        var dpiX = 96d * scaleX;
        var dpiY = 96d * scaleY;
        var width = Math.Max(1, (int)Math.Ceiling(target.ActualWidth * scaleX));
        var height = Math.Max(1, (int)Math.Ceiling(target.ActualHeight * scaleY));
        var bitmap = new RenderTargetBitmap(width, height, dpiX, dpiY, PixelFormats.Pbgra32);
        bitmap.Render(target);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(path))
            encoder.Save(stream);

        var stride = width * 4;
        var pixels = new byte[stride * height];
        bitmap.CopyPixels(pixels, stride, 0);
        var nonBackground = CountNonBackgroundPixels(pixels);
        var limitations = Math.Abs(dpiX - DialogPaneVisualEvidenceCatalog.TargetDpi) <= 0.5 &&
            Math.Abs(dpiY - DialogPaneVisualEvidenceCatalog.TargetDpi) <= 0.5
            ? Array.Empty<string>()
            : [$"WPF desktop DPI is {dpiX:0.##}x{dpiY:0.##}; the 96-DPI target could not be forced without changing the active desktop session."];
        return new CaptureRaster(width, height, dpiX, dpiY, nonBackground, limitations);
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

    private static IReadOnlyList<DialogPaneVisualEvidenceButton> Buttons(DependencyObject root) =>
        Descendants(root).OfType<Button>()
            .Select(button => new DialogPaneVisualEvidenceButton(
                NormalizeLabel(button.Content?.ToString()),
                button.IsEnabled,
                button.IsDefault,
                button.IsCancel))
            .Where(button => !string.IsNullOrWhiteSpace(button.Label))
            .ToArray();

    private static IReadOnlyList<DialogPaneVisualEvidenceControlState> Controls(DependencyObject root) =>
        Descendants(root).OfType<Control>()
            .Select(ToControlState)
            .Where(state => state is not null)
            .Cast<DialogPaneVisualEvidenceControlState>()
            .ToArray();

    private static DialogPaneVisualEvidenceControlState? ToControlState(Control control)
    {
        return control switch
        {
            Button button => new("button", NormalizeLabel(button.Content?.ToString()), button.IsEnabled),
            CheckBox check => new("checkbox", NormalizeLabel(check.Content?.ToString()), check.IsEnabled, check.IsChecked),
            RadioButton radio => new("radio", NormalizeLabel(radio.Content?.ToString()), radio.IsEnabled, radio.IsChecked),
            ComboBox combo => new("combobox", NormalizeLabel(AutomationProperties.GetName(combo)), combo.IsEnabled, null, combo.SelectedIndex >= 0),
            TextBox box => new("textbox", NormalizeLabel(AutomationProperties.GetName(box)), box.IsEnabled),
            _ => null,
        };
    }

    private static (string Role, string Label) DescribeFocus(IInputElement? focused) => focused switch
    {
        Button button => ("button", NormalizeLabel(button.Content?.ToString())),
        CheckBox check => ("checkbox", NormalizeLabel(check.Content?.ToString())),
        RadioButton radio => ("radio", NormalizeLabel(radio.Content?.ToString())),
        ComboBox combo => ("combobox", NormalizeLabel(AutomationProperties.GetName(combo))),
        TextBox box => ("textbox", NormalizeLabel(AutomationProperties.GetName(box))),
        FrameworkElement element => (element.GetType().Name.ToLowerInvariant(), NormalizeLabel(AutomationProperties.GetName(element))),
        _ => (string.Empty, string.Empty),
    };

    private static string NormalizeLabel(string? label) =>
        (label ?? string.Empty).Trim().TrimEnd(':');

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
        int PixelWidth,
        int PixelHeight,
        double DpiX,
        double DpiY,
        long NonBackgroundPixelCount,
        IReadOnlyList<string> Limitations);

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
