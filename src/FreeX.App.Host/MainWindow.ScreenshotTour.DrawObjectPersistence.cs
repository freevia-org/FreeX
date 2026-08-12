using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private async Task CaptureDrawObjectPersistenceTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteDrawObjectPersistenceTourEvidence(outputDir);

        var savedWorkbookPath = Path.Combine(outputDir, DrawObjectPersistenceTourSavedWorkbookFileName);
        DeleteIfExists(savedWorkbookPath);

        WindowState = WindowState.Normal;
        Width = 1220;
        Height = 780;
        await Task.Delay(700);

        var seededContext = EnsureDrawObjectFormattingTourContext();
        var context = CreateDrawObjectPersistenceContext(
            seededContext.Sheet,
            seededContext.Shape,
            seededContext.Picture,
            seededContext.TextBox,
            savedWorkbookPath: string.Empty,
            savedWorkbookBytes: 0,
            persistenceStage: "seeded");
        var captures = new List<DrawObjectPersistenceTourManifestCapture>();
        var submittedMutations = new List<string>();

        try
        {
            _options.ObjectsDisplay = AppOptionsObjectDisplay.All;
            HideStartScreen();
            _workbook.Name = "Draw Object Persistence";
            MarkWorkbookDirty();
            UpdateTitleBar();

            SubmitDrawObjectPersistenceShapeMutations(context, submittedMutations);
            SelectDrawObjectPersistenceShape(context);
            captures.Add(await CaptureDrawObjectPersistenceWindowStateAsync(
                outputDir,
                context,
                "mutated-shape-result",
                "freex_draw_object_persistence_mutated_shape_result",
                "Selected shape shows submitted fill, outline, gradient, effect, size, rotation, and alt-text model state after command-bus mutations.",
                "after-shape-mutation"));

            SubmitDrawObjectPersistencePictureCropMutations(context, submittedMutations);
            SelectDrawObjectPersistencePicture(context);
            captures.Add(await CaptureDrawObjectPersistenceWindowStateAsync(
                outputDir,
                context,
                "picture-crop-result",
                "freex_draw_object_persistence_picture_crop_result",
                "Selected picture placeholder shows submitted size, rotation, lock-aspect-ratio, alt text, and non-zero crop state.",
                "after-picture-crop"));

            ExecuteDrawObjectPersistenceCommand(
                new SetPictureCropCommand(context.Sheet.Id, context.Picture.Id, 0, 0, 0, 0),
                "Reset Crop",
                submittedMutations);
            captures.Add(await CaptureDrawObjectPersistenceWindowStateAsync(
                outputDir,
                context,
                "picture-reset-crop-result",
                "freex_draw_object_persistence_picture_reset_crop_result",
                "Reset Crop submitted through SetPictureCropCommand and returned the selected picture crop values to zero.",
                "after-reset-crop"));

            SubmitDrawObjectPersistenceTextBoxMutations(context, submittedMutations);
            SelectDrawObjectPersistenceTextBox(context);
            captures.Add(await CaptureDrawObjectPersistenceWindowStateAsync(
                outputDir,
                context,
                "mutated-text-box-result",
                "freex_draw_object_persistence_mutated_text_box_result",
                "Selected text box shows submitted fill, outline, size, rotation, alt text, and moved anchor state.",
                "after-text-box-mutation"));

            SubmitDrawObjectPersistenceArrangeMutations(context, submittedMutations);
            captures.Add(await CaptureDrawObjectPersistenceSelectionPaneAsync(outputDir, context));

            context = await SaveDrawObjectPersistenceWorkbookAsync(savedWorkbookPath, context);
            captures.Add(await CaptureDrawObjectPersistenceWindowStateAsync(
                outputDir,
                context,
                "saved-native-workbook",
                "freex_draw_object_persistence_saved_native_workbook",
                "SaveWorkbookToTargetAsync wrote the native FreeX workbook while the submitted drawing-object state remains visible.",
                "saved"));

            await OpenFileAsync(savedWorkbookPath);
            context = ResolveDrawObjectPersistenceCurrentContext(savedWorkbookPath, "after-reopen");
            _options.ObjectsDisplay = AppOptionsObjectDisplay.All;
            SelectDrawObjectPersistenceShape(context);
            captures.Add(await CaptureDrawObjectPersistenceWindowStateAsync(
                outputDir,
                context,
                "reopened-persisted-objects",
                "freex_draw_object_persistence_reopened_persisted_objects",
                "OpenFileAsync reopened the saved native FreeX workbook and restored shape, picture, text-box, z-order, crop reset, and alt-text state.",
                "after-reopen"));

            ValidateDrawObjectPersistenceTourEvidence(outputDir, captures, savedWorkbookPath);
            await WriteDrawObjectPersistenceTourManifestAsync(outputDir, context, captures, submittedMutations);
        }
        catch
        {
            DeleteDrawObjectPersistenceTourEvidence(outputDir);
            throw;
        }
    }

    private void SubmitDrawObjectPersistenceShapeMutations(
        DrawObjectPersistenceTourContext context,
        List<string> submittedMutations)
    {
        ExecuteDrawObjectPersistenceCommand(
            new SetDrawingShapeColorsCommand(context.Sheet.Id, context.Shape.Id, new CellColor(112, 48, 160), new CellColor(255, 192, 0)),
            "Shape Fill/Outline",
            submittedMutations);
        ExecuteDrawObjectPersistenceCommand(
            new SetDrawingShapeGradientCommand(context.Sheet.Id, context.Shape.Id, new CellColor(112, 48, 160), new CellColor(0, 176, 240), DrawingShapeGradientDirection.Vertical),
            "Shape Gradient",
            submittedMutations);
        ExecuteDrawObjectPersistenceCommand(
            new SetDrawingShapeEffectCommand(context.Sheet.Id, context.Shape.Id, DrawingShapeEffectPreset.Glow),
            "Shape Effects",
            submittedMutations);
        ExecuteDrawObjectPersistenceCommand(
            new ResizeDrawingShapeCommand(context.Sheet.Id, context.Shape.Id, 220, 116),
            "Object Size",
            submittedMutations);
        ExecuteDrawObjectPersistenceCommand(
            new RotateDrawingShapeCommand(context.Sheet.Id, context.Shape.Id, 24),
            "Rotate Shape",
            submittedMutations);
        ExecuteDrawObjectPersistenceCommand(
            new SetDrawingShapeAltTextCommand(context.Sheet.Id, context.Shape.Id, "Persisted shape alt text from submitted command."),
            "Shape Alt Text",
            submittedMutations);
    }

    private void SubmitDrawObjectPersistencePictureCropMutations(
        DrawObjectPersistenceTourContext context,
        List<string> submittedMutations)
    {
        ExecuteDrawObjectPersistenceCommand(
            new ResizePictureCommand(context.Sheet.Id, context.Picture.Id, 214, 132),
            "Resize Picture",
            submittedMutations);
        ExecuteDrawObjectPersistenceCommand(
            new RotatePictureCommand(context.Sheet.Id, context.Picture.Id, 18),
            "Rotate Picture",
            submittedMutations);
        ExecuteDrawObjectPersistenceCommand(
            new SetPictureLockAspectRatioCommand(context.Sheet.Id, context.Picture.Id, false),
            "Picture Lock Aspect Ratio",
            submittedMutations);
        ExecuteDrawObjectPersistenceCommand(
            new SetPictureCropCommand(context.Sheet.Id, context.Picture.Id, 0.16, 0.08, 0.12, 0.05),
            "Crop Picture",
            submittedMutations);
        ExecuteDrawObjectPersistenceCommand(
            new SetPictureAltTextCommand(context.Sheet.Id, context.Picture.Id, "Persisted picture alt text from submitted command."),
            "Picture Alt Text",
            submittedMutations);
        ExecuteDrawObjectPersistenceCommand(
            new RepositionPictureCommand(context.Sheet.Id, context.Picture.Id, new CellAddress(context.Sheet.Id, 4, 5)),
            "Move Picture",
            submittedMutations);
    }

    private void SubmitDrawObjectPersistenceTextBoxMutations(
        DrawObjectPersistenceTourContext context,
        List<string> submittedMutations)
    {
        ExecuteDrawObjectPersistenceCommand(
            new SetTextBoxColorsCommand(context.Sheet.Id, context.TextBox.Id, new CellColor(226, 239, 218), new CellColor(84, 130, 53)),
            "Text Box Colors",
            submittedMutations);
        ExecuteDrawObjectPersistenceCommand(
            new ResizeTextBoxCommand(context.Sheet.Id, context.TextBox.Id, 240, 88),
            "Resize Text Box",
            submittedMutations);
        ExecuteDrawObjectPersistenceCommand(
            new RotateTextBoxCommand(context.Sheet.Id, context.TextBox.Id, 345),
            "Rotate Text Box",
            submittedMutations);
        ExecuteDrawObjectPersistenceCommand(
            new SetTextBoxAltTextCommand(context.Sheet.Id, context.TextBox.Id, "Persisted text box alt text from submitted command."),
            "Text Box Alt Text",
            submittedMutations);
        ExecuteDrawObjectPersistenceCommand(
            new RepositionTextBoxCommand(context.Sheet.Id, context.TextBox.Id, new CellAddress(context.Sheet.Id, 8, 4)),
            "Move Text Box",
            submittedMutations);
    }

    private void SubmitDrawObjectPersistenceArrangeMutations(
        DrawObjectPersistenceTourContext context,
        List<string> submittedMutations)
    {
        ExecuteDrawObjectPersistenceCommand(
            new MoveSelectionPaneObjectCommand(context.Sheet.Id, SelectionPaneObjectKind.Shape, context.Shape.Id, forward: true),
            "Bring Forward",
            submittedMutations);
        ExecuteDrawObjectPersistenceCommand(
            new MoveSelectionPaneObjectCommand(context.Sheet.Id, SelectionPaneObjectKind.TextBox, context.TextBox.Id, forward: false),
            "Send Backward",
            submittedMutations);
        ExecuteDrawObjectPersistenceCommand(
            new RenameSelectionPaneObjectCommand(context.Sheet.Id, SelectionPaneObjectKind.Shape, context.Shape.Id, "Persisted Shape"),
            "Selection Pane Rename",
            submittedMutations);
    }

    private void ExecuteDrawObjectPersistenceCommand(
        IWorkbookCommand command,
        string title,
        List<string> submittedMutations)
    {
        if (!TryExecuteCommand(command, title, out var outcome))
            throw new InvalidOperationException(outcome.ErrorMessage ?? $"Draw/object persistence tour command '{title}' failed.");

        submittedMutations.Add($"{command.GetType().Name}:{title}");
    }

    private async Task<DrawObjectPersistenceTourContext> SaveDrawObjectPersistenceWorkbookAsync(
        string savedWorkbookPath,
        DrawObjectPersistenceTourContext context)
    {
        var adapter = FileFormatResolver.FindSaveAdapter(_fileAdapters, ".fxl", out _)
            ?? throw new InvalidOperationException("Draw/object persistence tour could not find the native FreeX save adapter.");
        var saved = await SaveWorkbookToTargetAsync(new FileSaveTarget(savedWorkbookPath, adapter));
        if (!saved)
            throw new InvalidOperationException("Draw/object persistence tour could not save the native FreeX workbook.");

        return CreateDrawObjectPersistenceContext(
            context.Sheet,
            context.Shape,
            context.Picture,
            context.TextBox,
            savedWorkbookPath,
            new FileInfo(savedWorkbookPath).Length,
            "saved");
    }

    private void SelectDrawObjectPersistenceShape(DrawObjectPersistenceTourContext context) =>
        SelectDrawObjectFormattingObject(context.Shape.Anchor, context.Shape.Id, FreeX.App.UI.ObjectKind.Shape);

    private void SelectDrawObjectPersistencePicture(DrawObjectPersistenceTourContext context) =>
        SelectDrawObjectFormattingObject(context.Picture.Anchor, context.Picture.Id, FreeX.App.UI.ObjectKind.Picture);

    private void SelectDrawObjectPersistenceTextBox(DrawObjectPersistenceTourContext context) =>
        SelectDrawObjectFormattingObject(context.TextBox.Anchor, context.TextBox.Id, FreeX.App.UI.ObjectKind.TextBox);

    private DrawObjectPersistenceTourContext ResolveDrawObjectPersistenceCurrentContext(
        string savedWorkbookPath,
        string persistenceStage)
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("Draw/object persistence tour could not resolve the reopened worksheet.");
        var shape = sheet.DrawingShapes.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, "Persisted Shape", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Draw/object persistence tour could not resolve the persisted shape.");
        var picture = sheet.Pictures.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, "Tour Picture Logo", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Draw/object persistence tour could not resolve the persisted picture.");
        var textBox = sheet.TextBoxes.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, "Tour Text Box", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Draw/object persistence tour could not resolve the persisted text box.");

        return CreateDrawObjectPersistenceContext(
            sheet,
            shape,
            picture,
            textBox,
            savedWorkbookPath,
            File.Exists(savedWorkbookPath) ? new FileInfo(savedWorkbookPath).Length : 0,
            persistenceStage);
    }

    private static DrawObjectPersistenceTourContext CreateDrawObjectPersistenceContext(
        Sheet sheet,
        DrawingShapeModel shape,
        PictureModel picture,
        FreeX.Core.Model.TextBoxModel textBox,
        string savedWorkbookPath,
        long savedWorkbookBytes,
        string persistenceStage) =>
        new(
            Sheet: sheet,
            Shape: shape,
            Picture: picture,
            TextBox: textBox,
            SavedWorkbookPath: savedWorkbookPath,
            SavedWorkbookOutputFileName: string.IsNullOrWhiteSpace(savedWorkbookPath) ? string.Empty : Path.GetFileName(savedWorkbookPath),
            SavedWorkbookBytes: savedWorkbookBytes,
            PersistenceStage: persistenceStage);

    private async Task<DrawObjectPersistenceTourManifestCapture> CaptureDrawObjectPersistenceWindowStateAsync(
        string outputDir,
        DrawObjectPersistenceTourContext context,
        string state,
        string fileName,
        string evidenceSummary,
        string persistenceStage)
    {
        SelectRibbonTourTab(RibbonScreenshotTourPlanner.DefaultTabs.Single(tab => tab.Header == "Draw"));
        UpdateViewport();
        RefreshToolbar();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await CaptureCurrentWindowAsync(outputDir, fileName, 760);
        return CreateDrawObjectPersistenceCapture(
            context,
            state,
            fileName,
            "Draw workbook window",
            "RenderTargetBitmap-window-full",
            ActualWidth,
            Math.Min(ActualHeight, 760),
            evidenceSummary,
            persistenceStage,
            []);
    }

    private async Task<DrawObjectPersistenceTourManifestCapture> CaptureDrawObjectPersistenceSelectionPaneAsync(
        string outputDir,
        DrawObjectPersistenceTourContext context)
    {
        var dialog = new SelectionPaneDialog(SelectionPaneDialog.BuildItems(context.Sheet)) { Owner = this };
        try
        {
            dialog.Show();
            dialog.Activate();
            await Task.Delay(300);
            dialog.UpdateLayout();
            await CaptureWindowElementForScreenshotTourAsync(dialog, outputDir, "freex_draw_object_persistence_selection_pane_arranged");
            return CreateDrawObjectPersistenceCapture(
                context,
                "selection-pane-arranged",
                "freex_draw_object_persistence_selection_pane_arranged",
                "Selection Pane",
                "RenderTargetBitmap-selection-pane-dialog-window",
                dialog.ActualWidth,
                dialog.ActualHeight,
                "Selection Pane reflects submitted rename plus bring-forward/send-backward z-order after command-bus mutations.",
                "after-arrange",
                SelectionPaneDialog.BuildItems(context.Sheet).Select(item => $"{item.Kind}:{item.Name}:{item.IsVisible}").ToArray());
        }
        finally
        {
            dialog.Close();
        }
    }

    private DrawObjectPersistenceTourManifestCapture CreateDrawObjectPersistenceCapture(
        DrawObjectPersistenceTourContext context,
        string state,
        string fileName,
        string surface,
        string captureMethod,
        double captureLogicalWidth,
        double captureLogicalHeight,
        string evidenceSummary,
        string persistenceStage,
        IReadOnlyList<string> visibleSelectionPaneItems)
    {
        var selectedObjectKind = SheetGrid?.SelectedObjectKind.ToString() ?? string.Empty;
        var selectedObjectName = GetDrawObjectFormattingSelectedObjectName(context.Sheet);
        return new DrawObjectPersistenceTourManifestCapture(
            CaptureKey: $"draw-object-persistence:{state}",
            PairKey: $"interactive:draw-object-persistence:{state}",
            CatalogIds:
            [
                "UI-CAT-DRAW-001",
                "UI-CAT-DRAW-001A",
                "UI-CAT-DRAW-001B",
                "UI-CAT-DRAW-001C",
                "UI-CMD-DRAW-001",
                "UI-CMD-DRAW-002",
                "UI-CMD-DRAW-003",
                "UI-CMD-DRAW-004",
                "UI-CMD-DRAW-005"
            ],
            State: state,
            Surface: surface,
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: captureMethod,
            CaptureLogicalWidth: captureLogicalWidth,
            CaptureLogicalHeight: captureLogicalHeight,
            SheetName: context.Sheet.Name,
            SelectedRange: SheetGrid?.SelectedRange?.ToString() ?? string.Empty,
            SelectedObjectKind: selectedObjectKind,
            SelectedObjectName: selectedObjectName,
            ShapeSnapshot: DescribeShape(context.Shape),
            PictureSnapshot: DescribePicture(context.Picture),
            TextBoxSnapshot: DescribeTextBox(context.TextBox),
            DrawingZOrder: context.Sheet.DrawingObjectZOrder.Select(entry => $"{entry.Kind}:{entry.Id:N}").ToArray(),
            PersistenceStage: persistenceStage,
            SavedWorkbookOutputFileName: context.SavedWorkbookOutputFileName,
            SavedWorkbookBytes: context.SavedWorkbookBytes,
            VisibleSelectionPaneItems: visibleSelectionPaneItems,
            EvidenceSummary: evidenceSummary);
    }

    private static string DescribeShape(DrawingShapeModel shape) =>
        string.Join(
            "|",
            $"name={shape.Name}",
            $"anchor={shape.Anchor.ToA1()}",
            $"size={shape.Width:0.#}x{shape.Height:0.#}",
            $"rotation={shape.RotationDegrees:0.#}",
            $"fill={DescribeColor(shape.FillColor)}",
            $"outline={DescribeColor(shape.OutlineColor)}",
            $"gradientEnd={DescribeColor(shape.GradientFillEndColor)}",
            $"gradientDirection={shape.GradientFillDirection}",
            $"effect={shape.GetEffectiveEffectPreset()}",
            $"alt={shape.AltText}");

    private static string DescribePicture(PictureModel picture) =>
        string.Join(
            "|",
            $"name={picture.Name}",
            $"anchor={picture.Anchor.ToA1()}",
            $"size={picture.Width:0.#}x{picture.Height:0.#}",
            $"rotation={picture.RotationDegrees:0.#}",
            $"lockAspect={picture.LockAspectRatio}",
            $"crop={picture.CropLeft:0.##},{picture.CropTop:0.##},{picture.CropRight:0.##},{picture.CropBottom:0.##}",
            $"alt={picture.AltText}");

    private static string DescribeTextBox(FreeX.Core.Model.TextBoxModel textBox) =>
        string.Join(
            "|",
            $"name={textBox.Name}",
            $"anchor={textBox.Anchor.ToA1()}",
            $"size={textBox.Width:0.#}x{textBox.Height:0.#}",
            $"rotation={textBox.RotationDegrees:0.#}",
            $"fill={DescribeColor(textBox.FillColor)}",
            $"outline={DescribeColor(textBox.OutlineColor)}",
            $"alt={textBox.AltText}");

    private static string DescribeColor(CellColor? color) =>
        color is null ? "none" : $"{color.Value.R},{color.Value.G},{color.Value.B}";

    private static void DeleteDrawObjectPersistenceTourEvidence(string outputDir)
    {
        if (!Directory.Exists(outputDir))
            return;

        foreach (var file in Directory.EnumerateFiles(outputDir, "freex_draw_object_persistence_*.png"))
            File.Delete(file);

        DeleteIfExists(Path.Combine(outputDir, DrawObjectPersistenceTourManifestFileName));
        DeleteIfExists(Path.Combine(outputDir, DrawObjectPersistenceTourSavedWorkbookFileName));
    }

    private static void ValidateDrawObjectPersistenceTourEvidence(
        string outputDir,
        IReadOnlyList<DrawObjectPersistenceTourManifestCapture> captures,
        string savedWorkbookPath)
    {
        if (captures.Count != 7)
            throw new InvalidOperationException($"Draw/object persistence tour expected 7 captures but created {captures.Count}.");

        var missing = captures
            .Select(capture => capture.OutputFileName)
            .Where(fileName => !File.Exists(Path.Combine(outputDir, fileName)))
            .ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException(
                $"Draw/object persistence tour did not create {missing.Length} planned capture(s): {string.Join(", ", missing)}.");

        var blank = captures
            .Select(capture => capture.OutputFileName)
            .Where(fileName => !IsNonBlankPng(Path.Combine(outputDir, fileName)))
            .ToArray();
        if (blank.Length > 0)
            throw new InvalidOperationException(
                $"Draw/object persistence tour created blank capture(s): {string.Join(", ", blank)}.");

        if (!File.Exists(savedWorkbookPath) || new FileInfo(savedWorkbookPath).Length <= 0)
            throw new InvalidOperationException("Draw/object persistence tour did not retain a non-empty native FreeX workbook.");
    }

    private static async Task WriteDrawObjectPersistenceTourManifestAsync(
        string outputDir,
        DrawObjectPersistenceTourContext context,
        IReadOnlyList<DrawObjectPersistenceTourManifestCapture> captures,
        IReadOnlyList<string> submittedMutations)
    {
        var plannedCaptures = new[]
        {
            "mutated-shape-result",
            "picture-crop-result",
            "picture-reset-crop-result",
            "mutated-text-box-result",
            "selection-pane-arranged",
            "saved-native-workbook",
            "reopened-persisted-objects"
        };

        var manifest = new DrawObjectPersistenceTourManifest(
            Tool: "FREEX_DRAW_OBJECT_PERSISTENCE_TOUR",
            EvidenceFamily: "draw-object-persistence",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "draw-object-persistence:submitted-mutation-visual-evidence",
            OutputDirectory: outputDir,
            OutputNaming: "freex_draw_object_persistence_<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            CatalogIds:
            [
                "UI-CAT-DRAW-001",
                "UI-CAT-DRAW-001A",
                "UI-CAT-DRAW-001B",
                "UI-CAT-DRAW-001C",
                "UI-CMD-DRAW-001",
                "UI-CMD-DRAW-002",
                "UI-CMD-DRAW-003",
                "UI-CMD-DRAW-004",
                "UI-CMD-DRAW-005"
            ],
            SheetName: context.Sheet.Name,
            SavedWorkbookPath: context.SavedWorkbookPath,
            SavedWorkbookOutputFileName: context.SavedWorkbookOutputFileName,
            SavedWorkbookBytes: context.SavedWorkbookBytes,
            PersistencePath: "SaveWorkbookToTargetAsync(.fxl native FreeX adapter) then OpenFileAsync(saved .fxl)",
            CaptureStatus: "complete",
            CaptureMode: IsScreenshotTourBackgroundRenderAllowed()
                ? "background-render-opt-in"
                : "foreground-guarded-render",
            PlannedCaptureCount: plannedCaptures.Length,
            ActualCaptureCount: captures.Count,
            PlannedCaptures: plannedCaptures,
            Pairing: new DrawObjectPersistenceTourManifestPairing(
                "interactive:draw-object-persistence:<State>",
                "excel",
                "not-yet-wired",
                "not-yet-captured"),
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 allowed in-process RenderTargetBitmap capture; no foreground mouse, keyboard, drag-handle, crop-handle, or screen capture input was used."
                    : "Window and dialog captures abort unless the expected FreeX WPF surface owns foreground focus immediately before render and file write."),
            Captures: captures,
            SubmittedMutations: submittedMutations,
            CoveredStates:
            [
                "Selected shape after submitted fill, outline, gradient, effect, size, rotation, and alt-text commands.",
                "Selected picture placeholder after submitted size, rotation, lock-aspect, crop, move, and alt-text commands.",
                "Selected picture placeholder after submitted Reset Crop command returns crop values to zero.",
                "Selected text box after submitted fill, outline, size, rotation, move, and alt-text commands.",
                "Selection Pane after submitted rename plus bring-forward/send-backward z-order mutations.",
                "Saved native FreeX workbook after submitted drawing-object mutations.",
                "Reopened native FreeX workbook with persisted drawing-object mutation state."
            ],
            Limitations:
            [
                "The tour uses command-bus and host save/open service paths; it does not synthesize foreground mouse selection, drag handles, crop handles, keytips, UIA Invoke, or dialog OK/access-key input.",
                "Picture evidence uses the current deterministic picture placeholder bytes rather than a native file-picker image import.",
                "Crop handle manipulation remains a foreground-input gap; this tour records SetPictureCropCommand and reset-crop result state only.",
                "Persistence is proven for the native FreeX .fxl adapter; XLSX drawing-object round-trip breadth remains a separate compatibility lane.",
                "No paired Microsoft Excel screenshots are produced by this tool."
            ]);

        var path = Path.Combine(outputDir, DrawObjectPersistenceTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.DrawObjectPersistenceTourManifest);
    }

    private sealed record DrawObjectPersistenceTourContext(
        Sheet Sheet,
        DrawingShapeModel Shape,
        PictureModel Picture,
        FreeX.Core.Model.TextBoxModel TextBox,
        string SavedWorkbookPath,
        string SavedWorkbookOutputFileName,
        long SavedWorkbookBytes,
        string PersistenceStage);

    private sealed record DrawObjectPersistenceTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        IReadOnlyList<string> CatalogIds,
        string SheetName,
        string SavedWorkbookPath,
        string SavedWorkbookOutputFileName,
        long SavedWorkbookBytes,
        string PersistencePath,
        string CaptureStatus,
        string CaptureMode,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        IReadOnlyList<string> PlannedCaptures,
        DrawObjectPersistenceTourManifestPairing Pairing,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        IReadOnlyList<DrawObjectPersistenceTourManifestCapture> Captures,
        IReadOnlyList<string> SubmittedMutations,
        IReadOnlyList<string> CoveredStates,
        IReadOnlyList<string> Limitations);

    private sealed record DrawObjectPersistenceTourManifestPairing(
        string PairKeyPattern,
        string CounterpartSubject,
        string CounterpartTool,
        string CounterpartOutputNaming);

    private sealed record DrawObjectPersistenceTourManifestCapture(
        string CaptureKey,
        string PairKey,
        IReadOnlyList<string> CatalogIds,
        string State,
        string Surface,
        string FileName,
        string OutputFileName,
        string CaptureMethod,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight,
        string SheetName,
        string SelectedRange,
        string SelectedObjectKind,
        string SelectedObjectName,
        string ShapeSnapshot,
        string PictureSnapshot,
        string TextBoxSnapshot,
        IReadOnlyList<string> DrawingZOrder,
        string PersistenceStage,
        string SavedWorkbookOutputFileName,
        long SavedWorkbookBytes,
        IReadOnlyList<string> VisibleSelectionPaneItems,
        string EvidenceSummary);
}
