using System.Runtime.InteropServices;
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
using FreeP.App.Avalonia;
using FreeP.App.Compositor;
using FreeP.VisualEvidence;

namespace FreeP.VisualEvidence.Avalonia;

internal static class AvaloniaDialogPaneVisualEvidenceCapture
{
    internal static bool TryParse(string[] args, out string? outputRoot, out string? scenarioId, out string? error)
    {
        var request = FreePVisualEvidenceCaptureOrchestration.ParseRequest(
            args,
            FreePVisualEvidenceRoutes.DialogPane,
            DialogPaneVisualEvidenceCatalog.All.Select(scenario => scenario.Id));
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
            FreePVisualEvidenceRoutes.DialogPane);

        anchor.Width = DialogPaneVisualEvidenceCatalog.LogicalShellWidth;
        anchor.Height = DialogPaneVisualEvidenceCatalog.LogicalShellHeight;
        anchor.Position = new PixelPoint(40, 40);
        anchor.Show();

        var run = await FreePVisualEvidenceCaptureOrchestration.RunScenariosAsync(
            DialogPaneVisualEvidenceCatalog.All,
            scenarioId,
            scenario => scenario.Id,
            outputPlan,
            logProgress: true,
            async scenario =>
            {
                Window? dialog = null;
                try
                {
                    var preparation = DialogPaneVisualEvidencePreparationSession.Create(scenario);
                    var access = anchor.CreateVisualCaptureAdapter();
                    var routeHost = new AvaloniaDialogPaneVisualEvidenceRouteHost(access);
                    var dialogAdapter = new AvaloniaDialogPaneVisualEvidenceAdapter(anchor);
                    var assertions = preparation.PrepareRoute(routeHost).ToList();
                    Window target = anchor;
                    if (scenario.SurfaceKind == DialogPaneVisualEvidenceSurfaceKind.Dialog)
                    {
                        dialog = preparation.CreateDialog(dialogAdapter, assertions);
                        dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                        dialog.Show(anchor);
                        target = dialog;
                    }

                    await PumpLayout();
                    preparation.PrepareLoadedDialogState(dialog, dialogAdapter, assertions);
                    target.Activate();
                    FocusFirstInputIfNeeded(target, preparation.Plan.FocusIntent);
                    await PumpLayout();

                    var metadataRoot = scenario.SurfaceKind == DialogPaneVisualEvidenceSurfaceKind.Dialog
                        ? target
                        : access.DialogMetadataRoot(scenario.RouteId);
                    if (scenario.RouteId == "review.comments-pane")
                    {
                        Descendants(metadataRoot).OfType<ScrollViewer>().FirstOrDefault()?.SetCurrentValue(
                            ScrollViewer.OffsetProperty,
                            default(Vector));
                        await PumpLayout();
                    }

                    var scenarioOutput = FreePVisualEvidenceCaptureOrchestration.CreateScenarioOutputPlan(
                        outputRoot,
                        FreePVisualEvidenceCaptureOrchestration.AvaloniaHost,
                        scenario.Id,
                        FreePVisualEvidenceRoutes.DialogPane);
                    var imagePath = scenarioOutput.ImagePath!;
                    var raster = Capture(target, imagePath);
                    var focus = DescribeFocus(target.FocusManager?.GetFocusedElement());
                    var comparisonPath = scenario.SurfaceKind == DialogPaneVisualEvidenceSurfaceKind.Dialog
                        ? imagePath
                        : scenarioOutput.ComparisonImagePath!;
                    var pixelTarget = DialogPaneVisualEvidenceCatalog.PixelTargetFor(scenario);
                    var comparisonRaster = ReferenceEquals(metadataRoot, target)
                        ? raster
                        : Capture(metadataRoot, comparisonPath, pixelTarget?.Width, pixelTarget?.Height);
                    var buttons = Buttons(metadataRoot);
                    var controls = Controls(metadataRoot);
                    assertions.AddRange(preparation.CompleteRoute(routeHost));

                    return new DialogPaneVisualEvidenceCapture(
                        scenario.Id,
                        scenario.RouteId,
                        scenario.StateId,
                        "avalonia",
                        raster.NonBackgroundPixelCount > 0 ? "complete" : "blocked",
                        scenarioOutput.ImageRelativePath!,
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
                        scenario.SurfaceKind == DialogPaneVisualEvidenceSurfaceKind.Dialog
                            ? scenarioOutput.ImageRelativePath!
                            : scenarioOutput.ComparisonImageRelativePath!,
                        comparisonRaster.LogicalWidth,
                        comparisonRaster.LogicalHeight);
                }
                finally
                {
                    dialog?.Close();
                    await PumpLayout();
                }
            },
            createBlockedCapture: BlockedCapture,
            createLimitation: (_, _) => null);

        anchor.Close();
        return FreePVisualEvidenceCaptureOrchestration.FinalizeHostRun(
            outputPlan,
            run,
            (captures, limitations) => new DialogPaneVisualEvidenceHostManifest(
                1,
                "avalonia",
                "visible-app-owned-render-target",
                DialogPaneVisualEvidenceCatalog.TargetDpi,
                DialogPaneVisualEvidenceCatalog.LogicalShellWidth,
                DialogPaneVisualEvidenceCatalog.LogicalShellHeight,
                FreePVisualEvidenceCaptureOrchestration.UtcTimestamp(),
                captures,
                limitations));
    }

    private sealed class AvaloniaDialogPaneVisualEvidenceAdapter(MainWindow owner)
        : IDialogPaneVisualEvidenceDialogAdapter<Window>
    {
        public Window CreateSlideSize(DialogPaneVisualEvidenceSlideSizePreparation preparation)
        {
            var dialog = new SlideSizeDialog(owner.Editor);
            if (preparation.InitialInput is { } input)
                dialog.SetInputForTests(input.WidthText, input.HeightText, input.Unit);
            return dialog;
        }

        public Window CreateHeaderFooter(DialogPaneVisualEvidenceHeaderFooterPreparation preparation)
        {
            var dialog = new HeaderFooterDialog(owner.Editor, preparation.InitialFocus);
            dialog.SetInputForTests(
                preparation.ShowDateTime,
                preparation.ShowFooter,
                preparation.ShowSlideNumber,
                preparation.FooterText);
            return dialog;
        }

        public Window CreateFindReplace(DialogPaneVisualEvidenceFindReplacePreparation preparation)
        {
            var dialog = new FindReplaceDialog(owner.Editor, preparation.ReplaceMode);
            dialog.SetInputForTests(
                preparation.Query,
                preparation.Replacement,
                preparation.MatchCase,
                preparation.WholeWord);
            return dialog;
        }

        public Window CreateHyperlink(
            DialogPaneVisualEvidenceHyperlinkPreparation preparation,
            DialogPaneVisualEvidenceFixture fixture) =>
            new HyperlinkDialog(
                fixture.Presentation.Slides,
                preparation.InitialLink?.ToModel());

        public Window CreateChartData(DialogPaneVisualEvidenceChartDataPreparation preparation) =>
            new ChartDataDialog(owner.Editor);

        public Window CreateCustomShows(DialogPaneVisualEvidenceCustomShowsPreparation preparation) =>
            new CustomShowDialog(new SlideShowCustomShowSession(() => owner.Editor));

        public bool ApplyHyperlinkValidation(
            Window dialog,
            DialogPaneVisualEvidenceHyperlinkInput input) =>
            Require<HyperlinkDialog>(dialog).ApplyInputForTests(
                input.TargetKind,
                input.Url,
                input.SelectedSlideIndex,
                input.Tooltip);

        public void PrepareCustomShowsValidation(Window dialog) =>
            Require<CustomShowDialog>(dialog).PrepareMissingNameForTests();

        public DialogPaneVisualEvidenceValidationResult PrepareSlideSizeLoadedState(Window dialog)
        {
            var slideSize = Require<SlideSizeDialog>(dialog);
            return new(slideSize.ApplyForTests(), slideSize.ValidationText);
        }

        public DialogPaneVisualEvidenceValidationResult PrepareChartDataLoadedState(Window dialog)
        {
            var chart = Require<ChartDataDialog>(dialog);
            return new(chart.PrepareInvalidValueForTests(), chart.ValidationText);
        }

        private static TDialog Require<TDialog>(Window dialog)
            where TDialog : Window =>
            dialog as TDialog ?? throw new InvalidOperationException(
                $"Expected {typeof(TDialog).Name}, but received {dialog.GetType().Name}.");
    }

    private sealed class AvaloniaDialogPaneVisualEvidenceRouteHost(MainWindow.AvaloniaVisualCaptureAdapter access)
        : IDialogPaneVisualEvidenceRouteHost
    {
        public IReadOnlyList<uint> SelectedShapeIds => access.SelectedShapeIds;
        public int SlideCount => access.SlideCount;
        public int CurrentShapeCount => access.CurrentShapeCount;
        public string? CurrentLayoutId => access.CurrentLayoutId;
        public bool IsTablePickerVisible => access.IsTablePickerVisible;
        public bool IsLayoutPickerVisible => access.IsLayoutPickerVisible;
        public DialogPaneVisualEvidenceChoiceState ChoiceState => new(
            access.TableChoiceCount,
            access.DefaultTableChoiceCount,
            access.CurrentLayoutChoiceCount,
            access.DisabledLayoutChoiceCount);

        public void LoadPresentation(FreeP.Core.Model.Presentation presentation) => access.LoadPresentation(presentation);
        public void SelectShape(uint shapeId) => access.SelectShape(shapeId);
        public void RefreshCanvas() => access.RefreshCanvas();
        public void ShowReviewCommentsPane() => access.ShowCommentsPane();
        public void SelectFirstReviewComment() => access.SelectFirstComment();
        public void ShowAccessibilityCheckerPane() => access.ShowAccessibilityPane();
        public void SelectFirstAccessibilityIssue() => access.SelectFirstAccessibilityIssue();
        public void ShowAltTextPane() => access.ShowAltTextPane();
        public void ShowReadingOrderPane() => access.ShowReadingOrderPane();
        public void ShowProofingPane() => access.ShowProofingPane();
        public void SelectFirstProofingIssue() => access.SelectFirstProofingIssue();
        public void ShowMediaCaptionPane() => access.ShowMediaCaptionPane();
        public void ShowSmartArtTextPane() => access.ShowSmartArtTextPane();
        public void EnsureAnimationPaneVisible() => access.EnsureAnimationPaneVisible();
        public void ShowPrintOptionsPane() => access.ShowPrintOptionsPane();
        public void OpenTablePicker() => access.OpenTablePicker();
        public void OpenLayoutPicker() => access.OpenLayoutPicker();
        public void HideTablePicker() => access.HideTablePicker();
        public void HideLayoutPicker() => access.HideLayoutPicker();
    }

    private static void FocusFirstInputIfNeeded(
        Window target,
        DialogPaneVisualEvidenceFocusIntent focusIntent)
    {
        if (focusIntent != DialogPaneVisualEvidenceFocusIntent.PreserveNativeOrFirstEditable)
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
        CheckBox check => new("checkbox", FreePVisualEvidenceCaptureOrchestration.NormalizeLabel(null, check.Content?.ToString()), check.IsEnabled, check.IsChecked),
        RadioButton radio => new("radio", FreePVisualEvidenceCaptureOrchestration.NormalizeLabel(null, radio.Content?.ToString()), radio.IsEnabled, radio.IsChecked),
        Button button when button is not ToggleButton && ToButton(button) is { } action => new("button", action.ActionId, button.IsEnabled),
        ComboBox combo => new("combobox", FreePVisualEvidenceCaptureOrchestration.NormalizeLabel(AutomationProperties.GetName(combo)), combo.IsEnabled, null, combo.SelectedIndex >= 0),
        TextBox box => new("textbox", FreePVisualEvidenceCaptureOrchestration.NormalizeLabel(AutomationProperties.GetName(box)), box.IsEnabled),
        _ => null,
    };

    private static (string Role, string Label) DescribeFocus(IInputElement? focused) => focused switch
    {
        CheckBox check => ("checkbox", FreePVisualEvidenceCaptureOrchestration.NormalizeLabel(null, check.Content?.ToString())),
        RadioButton radio => ("radio", FreePVisualEvidenceCaptureOrchestration.NormalizeLabel(null, radio.Content?.ToString())),
        Button button => ("button", FreePVisualEvidenceCaptureOrchestration.NormalizeLabel(AutomationProperties.GetName(button), button.Content?.ToString())),
        ComboBox combo => ("combobox", FreePVisualEvidenceCaptureOrchestration.NormalizeLabel(AutomationProperties.GetName(combo))),
        TextBox box => ("textbox", FreePVisualEvidenceCaptureOrchestration.NormalizeLabel(AutomationProperties.GetName(box))),
        _ => (string.Empty, string.Empty),
    };

    private static DialogPaneVisualEvidenceButton? ToButton(Button button)
    {
        var fallback = button.Content as string;
        var label = FreePVisualEvidenceCaptureOrchestration.NormalizeLabel(
            AutomationProperties.GetName(button),
            fallback);
        var automationId = FreePVisualEvidenceCaptureOrchestration.NormalizeLabel(
            AutomationProperties.GetAutomationId(button));
        var actionId = string.IsNullOrWhiteSpace(automationId)
            ? FreePVisualEvidenceCaptureOrchestration.SemanticActionId(label)
            : automationId;
        return string.IsNullOrWhiteSpace(actionId)
            ? null
            : new(actionId, label, button.IsEnabled, button.IsDefault, button.IsCancel);
    }

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
