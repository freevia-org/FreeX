using System.IO;
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
using FreeP.App.Host;
using FreeP.VisualEvidence;
using Free.ToolsShared;

namespace FreeP.VisualEvidence.Wpf;

internal static class WpfDialogPaneVisualEvidenceCapture
{
    internal static bool TryRun(string[] args, out int exitCode)
    {
        var request = FreePVisualEvidenceCaptureOrchestration.ParseRequest(
            args,
            FreePVisualEvidenceRoutes.DialogPane,
            DialogPaneVisualEvidenceCatalog.All.Select(scenario => scenario.Id));
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
            FreePVisualEvidenceRoutes.DialogPane);
        var run = VisualEvidenceCaptureOrchestrator.RunScenariosAsync(
            DialogPaneVisualEvidenceCatalog.All,
            scenarioId,
            scenario => scenario.Id,
            outputPlan,
            logProgress: true,
            scenario =>
            {
                var preparation = DialogPaneVisualEvidencePreparationSession.Create(scenario);
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
                    var access = owner.CreateVisualCaptureAdapter();
                    var routeHost = new WpfDialogPaneVisualEvidenceRouteHost(access);
                    var dialogAdapter = new WpfDialogPaneVisualEvidenceAdapter(owner);
                    var assertions = preparation.PrepareRoute(routeHost).ToList();
                    Window target = owner;

                    if (scenario.SurfaceKind == DialogPaneVisualEvidenceSurfaceKind.Dialog)
                    {
                        dialog = preparation.CreateDialog(dialogAdapter, assertions);
                        dialog.Owner = owner;
                        dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                        dialog.Show();
                        target = dialog;
                    }

                    PumpLayout(target);
                    preparation.PrepareLoadedDialogState(dialog, dialogAdapter, assertions);
                    target.Activate();
                    FocusFirstInputIfNeeded(target, preparation.Plan.FocusIntent);
                    PumpLayout(target);

                    var scenarioOutput = FreePVisualEvidenceCaptureOrchestration.CreateScenarioOutputPlan(
                        outputRoot,
                        FreePVisualEvidenceCaptureOrchestration.WpfHost,
                        scenario.Id,
                        FreePVisualEvidenceRoutes.DialogPane);
                    var imagePath = scenarioOutput.ImagePath!;
                    var captureRoot = scenario.SurfaceKind == DialogPaneVisualEvidenceSurfaceKind.Dialog
                        ? AppOwnedClientRoot(target)
                        : target.Content as FrameworkElement ?? target;
                    var raster = Capture(captureRoot, imagePath);
                    var metadataRoot = scenario.SurfaceKind == DialogPaneVisualEvidenceSurfaceKind.Dialog
                        ? target
                        : access.DialogMetadataRoot(scenario.RouteId);
                    var comparisonRoot = scenario.SurfaceKind == DialogPaneVisualEvidenceSurfaceKind.Dialog
                        ? captureRoot
                        : metadataRoot as FrameworkElement ?? captureRoot;
                    var comparisonPath = scenario.SurfaceKind == DialogPaneVisualEvidenceSurfaceKind.Dialog
                        ? imagePath
                        : scenarioOutput.ComparisonImagePath!;
                    var pixelTarget = DialogPaneVisualEvidenceCatalog.PixelTargetFor(scenario);
                    var comparisonRaster = ReferenceEquals(comparisonRoot, captureRoot)
                        ? raster
                        : Capture(comparisonRoot, comparisonPath, pixelTarget?.Width, pixelTarget?.Height);

                    var focus = DescribeFocus(Keyboard.FocusedElement);
                    var buttons = Buttons(metadataRoot);
                    var controls = Controls(metadataRoot);
                    assertions.AddRange(preparation.CompleteRoute(routeHost));
                    return Task.FromResult(new DialogPaneVisualEvidenceCapture(
                        scenario.Id,
                        scenario.RouteId,
                        scenario.StateId,
                        "wpf",
                        raster.NonBackgroundPixelCount > 0 ? "complete" : "blocked",
                        scenarioOutput.ImageRelativePath!,
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
                        scenario.SurfaceKind == DialogPaneVisualEvidenceSurfaceKind.Dialog
                            ? scenarioOutput.ImageRelativePath!
                            : scenarioOutput.ComparisonImageRelativePath!,
                        comparisonRaster.LogicalWidth,
                        comparisonRaster.LogicalHeight));
                }
                finally
                {
                    dialog?.Close();
                    owner.Close();
                    PumpDispatcher();
                }
            },
            createBlockedCapture: BlockedCapture,
            createLimitation: (_, _) => null)
            .GetAwaiter()
            .GetResult();

        return VisualEvidenceCaptureOrchestrator.FinalizeHostRun(
            outputPlan,
            run,
            (captures, limitations) => new DialogPaneVisualEvidenceHostManifest(
                1,
                "wpf",
                "visible-app-owned-render-target",
                DialogPaneVisualEvidenceCatalog.TargetDpi,
                DialogPaneVisualEvidenceCatalog.LogicalShellWidth,
                DialogPaneVisualEvidenceCatalog.LogicalShellHeight,
                FreePVisualEvidenceCaptureOrchestration.UtcTimestamp(),
                captures,
                limitations),
            FreePVisualEvidenceCaptureOrchestration.HostManifestJsonOptions);
    }

    private sealed class WpfDialogPaneVisualEvidenceAdapter(MainWindow owner)
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

    private sealed class WpfDialogPaneVisualEvidenceRouteHost(MainWindow.WpfVisualCaptureAdapter access)
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
            CheckBox check => new("checkbox", FreePVisualEvidenceCaptureOrchestration.NormalizeLabel(null, check.Content?.ToString()), check.IsEnabled, check.IsChecked),
            RadioButton radio => new("radio", FreePVisualEvidenceCaptureOrchestration.NormalizeLabel(null, radio.Content?.ToString()), radio.IsEnabled, radio.IsChecked),
            ComboBox combo => new("combobox", FreePVisualEvidenceCaptureOrchestration.NormalizeLabel(AutomationProperties.GetName(combo)), combo.IsEnabled, null, combo.SelectedIndex >= 0),
            TextBox box => new("textbox", FreePVisualEvidenceCaptureOrchestration.NormalizeLabel(AutomationProperties.GetName(box)), box.IsEnabled),
            _ => null,
        };
    }

    private static (string Role, string Label) DescribeFocus(IInputElement? focused) => focused switch
    {
        Button button => ("button", FreePVisualEvidenceCaptureOrchestration.NormalizeLabel(AutomationProperties.GetName(button), button.Content?.ToString())),
        CheckBox check => ("checkbox", FreePVisualEvidenceCaptureOrchestration.NormalizeLabel(null, check.Content?.ToString())),
        RadioButton radio => ("radio", FreePVisualEvidenceCaptureOrchestration.NormalizeLabel(null, radio.Content?.ToString())),
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
