using System.IO;
using System.Text.Json;
using System.Windows;
using FreeX.App.Presentation.DrawingUI;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private async Task CaptureInsertObjectPersistenceTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteInsertObjectPersistenceTourEvidence(outputDir);

        var savedWorkbookPath = Path.Combine(outputDir, InsertObjectPersistenceTourSavedWorkbookFileName);
        DeleteIfExists(savedWorkbookPath);

        WindowState = WindowState.Normal;
        Width = 1220;
        Height = 800;
        await Task.Delay(700);

        var captures = new List<InsertObjectPersistenceTourManifestCapture>();
        var plannedCaptures = CreateInsertObjectPersistencePlannedCaptures();

        try
        {
            var context = EnsureInsertObjectPersistenceTourContext();

            captures.Add(await CaptureInsertObjectPersistenceWindowStateAsync(
                outputDir,
                context,
                "seeded-context-state",
                "freex_insert_object_persistence_seeded_context_state",
                "Command-seeded hyperlink, threaded comment, note, shape, text box, and picture placeholder are visible before save.",
                "seeded"));

            SelectInsertObjectPersistenceObject(context.Shape.Anchor, context.Shape.Id, FreeX.App.UI.ObjectKind.Shape);
            captures.Add(await CaptureInsertObjectPersistenceWindowStateAsync(
                outputDir,
                context,
                "selected-shape-handles",
                "freex_insert_object_persistence_selected_shape_handles",
                "Rectangle shape is selected through the same GridView SelectedObjectId/SelectedObjectKind state used by object clicks, showing the object border and resize handles.",
                "selected-before-save"));

            SelectInsertObjectPersistenceObject(context.TextBox.Anchor, context.TextBox.Id, FreeX.App.UI.ObjectKind.TextBox);
            captures.Add(await CaptureInsertObjectPersistenceWindowStateAsync(
                outputDir,
                context,
                "selected-text-box-handles",
                "freex_insert_object_persistence_selected_text_box_handles",
                "Text box object is selected and rendered with object selection handles before persistence.",
                "selected-before-save"));

            SelectInsertObjectPersistenceObject(context.Picture.Anchor, context.Picture.Id, FreeX.App.UI.ObjectKind.Picture);
            captures.Add(await CaptureInsertObjectPersistenceWindowStateAsync(
                outputDir,
                context,
                "selected-picture-handles",
                "freex_insert_object_persistence_selected_picture_handles",
                "Picture placeholder inserted through InsertPictureCommand is selected and rendered with object selection handles before persistence.",
                "selected-before-save"));

            context = await SaveInsertObjectPersistenceWorkbookAsync(savedWorkbookPath, context);
            captures.Add(await CaptureInsertObjectPersistenceWindowStateAsync(
                outputDir,
                context,
                "saved-status",
                "freex_insert_object_persistence_saved_status",
                "Native FreeX .fxl save completed through SaveWorkbookToTargetAsync; title/status state references the saved workbook while the selected picture remains visible.",
                "saved"));

            await OpenFileAsync(savedWorkbookPath);
            context = ResolveInsertObjectPersistenceCurrentContext(savedWorkbookPath, "reopened");

            SetActiveCell(context.HyperlinkCell);
            EnsureCellVisible(context.Picture.Anchor);
            UpdateViewport();
            RefreshToolbar();
            RefreshReviewCommentNoteCommandStates();
            captures.Add(await CaptureInsertObjectPersistenceWindowStateAsync(
                outputDir,
                context,
                "reopened-context-state",
                "freex_insert_object_persistence_reopened_context_state",
                "Saved native FreeX workbook was reopened through OpenFileAsync; persisted hyperlink, threaded comment, note, shape, text box, and picture placeholder are present again.",
                "reopened"));

            SelectInsertObjectPersistenceObject(context.Picture.Anchor, context.Picture.Id, FreeX.App.UI.ObjectKind.Picture);
            captures.Add(await CaptureInsertObjectPersistenceWindowStateAsync(
                outputDir,
                context,
                "reopened-picture-handles",
                "freex_insert_object_persistence_reopened_picture_handles",
                "After reopening, the persisted picture placeholder can be selected again and the GridView renders object handles for the reloaded object.",
                "selected-after-reopen"));

            ValidateInsertObjectPersistenceTourEvidence(outputDir, captures, savedWorkbookPath);
            await WriteInsertObjectPersistenceTourManifestAsync(outputDir, context, plannedCaptures, captures);
        }
        catch
        {
            DeleteInsertObjectPersistenceTourEvidence(outputDir);
            throw;
        }
    }

    private InsertObjectPersistenceTourContext EnsureInsertObjectPersistenceTourContext()
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("Insert object persistence tour requires an active worksheet.");

        HideStartScreen();
        _currentSheetId = sheet.Id;
        _currentFilePath = null;
        _workbook.Name = "Insert object persistence";
        sheet.Name = "Insert Object Persistence";
        _options.ObjectsDisplay = AppOptionsObjectDisplay.All;

        for (uint row = 1; row <= 16; row++)
        {
            for (uint col = 1; col <= 8; col++)
            {
                var address = new CellAddress(sheet.Id, row, col);
                sheet.ClearCell(address);
                sheet.Comments.Remove(address);
                sheet.ThreadedComments.Remove(address);
                sheet.Hyperlinks.Remove(address);
                sheet.HyperlinkMetadata.Remove(address);
            }
        }

        sheet.DrawingShapes.Clear();
        sheet.TextBoxes.Clear();
        sheet.Pictures.Clear();
        sheet.DrawingObjectZOrder.Clear();

        SeedInsertObjectPersistenceLabels(sheet);

        var hyperlinkCell = new CellAddress(sheet.Id, 2, 2);
        var threadedCommentCell = new CellAddress(sheet.Id, 2, 3);
        var noteCell = new CellAddress(sheet.Id, 2, 4);
        var shapeAnchor = new CellAddress(sheet.Id, 5, 2);
        var textBoxAnchor = new CellAddress(sheet.Id, 5, 4);
        var pictureAnchor = new CellAddress(sheet.Id, 9, 2);

        ExecuteInsertObjectPersistenceCommand(
            new SetHyperlinkCommand(
                sheet.Id,
                hyperlinkCell,
                "https://freex.example/insert-object-persistence",
                "Persisted hyperlink",
                new HyperlinkMetadata(HyperlinkTargetKind.ExistingFileOrWebPage, "Insert object persistence", "")),
            "Insert Hyperlink");
        ExecuteInsertObjectPersistenceCommand(
            new SetThreadedCommentCommand(sheet.Id, threadedCommentCell, "Persisted threaded comment evidence"),
            "Insert Threaded Comment");
        ExecuteInsertObjectPersistenceCommand(
            new SetCommentCommand(sheet.Id, noteCell, "Persisted note evidence"),
            "Insert Note");
        ExecuteInsertObjectPersistenceCommand(
            DrawingInsertionPlanner.BuildShapeCommand(sheet.Id, shapeAnchor, DrawingShapeKind.Rectangle, width: 172, height: 92),
            "Insert Shape");
        ExecuteInsertObjectPersistenceCommand(
            DrawingInsertionPlanner.BuildTextBoxCommand(sheet.Id, textBoxAnchor, "Persisted text box", width: 214, height: 82),
            "Insert Text Box");
        ExecuteInsertObjectPersistenceCommand(
            PictureInsertionPlacementPlanner.CreateInsertPictureCommand(
                sheet.Id,
                pictureAnchor,
                [1, 2, 3, 4],
                "image/png"),
            "Insert Picture Placeholder");

        var shape = sheet.DrawingShapes.Single(candidate => candidate.Anchor.Equals(shapeAnchor));
        shape.Name = "Persistence Rectangle";
        shape.Title = "Inserted rectangle";
        shape.AltText = "Rectangle inserted by the persistence evidence tour.";
        shape.FillColor = new CellColor(91, 155, 213);
        shape.OutlineColor = new CellColor(31, 78, 121);

        var textBox = sheet.TextBoxes.Single(candidate => candidate.Anchor.Equals(textBoxAnchor));
        textBox.Name = "Persistence Text Box";
        textBox.AltText = "Text box inserted by the persistence evidence tour.";
        textBox.FillColor = new CellColor(255, 242, 204);
        textBox.OutlineColor = new CellColor(191, 143, 0);

        var picture = sheet.Pictures.Single(candidate => candidate.Anchor.Equals(pictureAnchor));
        picture.Name = "Persistence Picture Placeholder";
        picture.AltText = "Picture placeholder inserted by the persistence evidence tour.";

        sheet.DrawingObjectZOrder.Add(new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Shape, shape.Id));
        sheet.DrawingObjectZOrder.Add(new DrawingObjectZOrderEntry(SelectionPaneObjectKind.TextBox, textBox.Id));
        sheet.DrawingObjectZOrder.Add(new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Picture, picture.Id));

        SetActiveCell(hyperlinkCell);
        EnsureCellVisible(pictureAnchor);
        SelectRibbonTourTab(RibbonScreenshotTourPlanner.DefaultTabs.Single(tab => tab.Header == "Insert"));
        RefreshToolbar();
        RefreshReviewCommentNoteCommandStates();
        UpdateViewport();
        UpdateTitleBar();
        MarkWorkbookDirty();

        return new InsertObjectPersistenceTourContext(
            Sheet: sheet,
            Shape: shape,
            TextBox: textBox,
            Picture: picture,
            HyperlinkCell: hyperlinkCell,
            ThreadedCommentCell: threadedCommentCell,
            NoteCell: noteCell,
            SavedWorkbookPath: string.Empty,
            SavedWorkbookOutputFileName: string.Empty,
            SavedWorkbookBytes: 0,
            PersistenceStage: "seeded");
    }

    private static void SeedInsertObjectPersistenceLabels(Sheet sheet)
    {
        var labels = new (uint Row, uint Col, string Value)[]
        {
            (1, 1, "Evidence"),
            (1, 2, "Hyperlink"),
            (1, 3, "Threaded comment"),
            (1, 4, "Note"),
            (4, 1, "Objects"),
            (8, 1, "Picture")
        };

        foreach (var (row, col, value) in labels)
            sheet.SetCell(new CellAddress(sheet.Id, row, col), new TextValue(value));
    }

    private void ExecuteInsertObjectPersistenceCommand(IWorkbookCommand command, string title)
    {
        if (!TryExecuteCommand(command, title, out var outcome))
            throw new InvalidOperationException($"Insert object persistence tour failed to apply '{title}': {outcome.ErrorMessage}");
    }

    private async Task<InsertObjectPersistenceTourContext> SaveInsertObjectPersistenceWorkbookAsync(
        string savedWorkbookPath,
        InsertObjectPersistenceTourContext context)
    {
        var adapter = FileFormatResolver.FindSaveAdapter(_fileAdapters, ".fxl", out _)
            ?? throw new InvalidOperationException("Insert object persistence tour could not find the native FreeX save adapter.");
        if (!await SaveWorkbookToTargetAsync(new FileSaveTarget(savedWorkbookPath, adapter)))
            throw new InvalidOperationException("Insert object persistence tour could not save the native FreeX workbook.");

        return context with
        {
            SavedWorkbookPath = savedWorkbookPath,
            SavedWorkbookOutputFileName = Path.GetFileName(savedWorkbookPath),
            SavedWorkbookBytes = new FileInfo(savedWorkbookPath).Length,
            PersistenceStage = "saved"
        };
    }

    private InsertObjectPersistenceTourContext ResolveInsertObjectPersistenceCurrentContext(
        string savedWorkbookPath,
        string persistenceStage)
    {
        var sheet = _workbook.Sheets.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, "Insert Object Persistence", StringComparison.OrdinalIgnoreCase))
            ?? GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("Insert object persistence tour could not resolve the reopened worksheet.");

        _currentSheetId = sheet.Id;
        var shape = sheet.DrawingShapes.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, "Persistence Rectangle", StringComparison.OrdinalIgnoreCase))
            ?? sheet.DrawingShapes.FirstOrDefault()
            ?? throw new InvalidOperationException("Insert object persistence tour could not resolve the persisted shape.");
        var textBox = sheet.TextBoxes.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, "Persistence Text Box", StringComparison.OrdinalIgnoreCase))
            ?? sheet.TextBoxes.FirstOrDefault()
            ?? throw new InvalidOperationException("Insert object persistence tour could not resolve the persisted text box.");
        var picture = sheet.Pictures.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, "Persistence Picture Placeholder", StringComparison.OrdinalIgnoreCase))
            ?? sheet.Pictures.FirstOrDefault()
            ?? throw new InvalidOperationException("Insert object persistence tour could not resolve the persisted picture.");

        return new InsertObjectPersistenceTourContext(
            Sheet: sheet,
            Shape: shape,
            TextBox: textBox,
            Picture: picture,
            HyperlinkCell: new CellAddress(sheet.Id, 2, 2),
            ThreadedCommentCell: new CellAddress(sheet.Id, 2, 3),
            NoteCell: new CellAddress(sheet.Id, 2, 4),
            SavedWorkbookPath: savedWorkbookPath,
            SavedWorkbookOutputFileName: Path.GetFileName(savedWorkbookPath),
            SavedWorkbookBytes: File.Exists(savedWorkbookPath) ? new FileInfo(savedWorkbookPath).Length : 0,
            PersistenceStage: persistenceStage);
    }

    private void SelectInsertObjectPersistenceObject(
        CellAddress anchor,
        Guid objectId,
        FreeX.App.UI.ObjectKind kind)
    {
        SetActiveCell(anchor);
        EnsureCellVisible(anchor);
        if (SheetGrid is not null)
        {
            SheetGrid.SelectedRange = new GridRange(anchor, anchor);
            SheetGrid.SelectedRanges = null;
            SheetGrid.SelectedObjectId = objectId;
            SheetGrid.SelectedObjectKind = kind;
            SheetGrid.InvalidateVisual();
        }

        UpdateViewport();
        RefreshToolbar();
    }

    private async Task<InsertObjectPersistenceTourManifestCapture> CaptureInsertObjectPersistenceWindowStateAsync(
        string outputDir,
        InsertObjectPersistenceTourContext context,
        string state,
        string fileName,
        string evidenceSummary,
        string persistenceStage)
    {
        SelectRibbonTourTab(RibbonScreenshotTourPlanner.DefaultTabs.Single(tab => tab.Header == "Insert"));
        UpdateViewport();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await CaptureCurrentWindowAsync(outputDir, fileName, 800);
        return CreateInsertObjectPersistenceCapture(context, state, fileName, evidenceSummary, persistenceStage);
    }

    private InsertObjectPersistenceTourManifestCapture CreateInsertObjectPersistenceCapture(
        InsertObjectPersistenceTourContext context,
        string state,
        string fileName,
        string evidenceSummary,
        string persistenceStage)
    {
        var selectedObjectId = SheetGrid?.SelectedObjectId ?? Guid.Empty;
        var selectedObjectKind = SheetGrid?.SelectedObjectKind ?? FreeX.App.UI.ObjectKind.None;
        return new InsertObjectPersistenceTourManifestCapture(
            CaptureKey: $"insert-object-persistence:{state}",
            PairKey: $"interactive:insert-object-persistence:{state}",
            CatalogIds: ["UI-CAT-INSERT-003", "UI-CAT-INSERT-003A", "UI-CMD-INSERT-008", "UI-CMD-INSERT-009", "UI-CMD-INSERT-010"],
            State: state,
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: "RenderTargetBitmap-window-full",
            CaptureLogicalWidth: ActualWidth,
            CaptureLogicalHeight: Math.Min(ActualHeight, 800),
            SheetName: context.Sheet.Name,
            SelectedRange: SheetGrid?.SelectedRange?.ToString() ?? string.Empty,
            SelectedObjectKind: selectedObjectKind.ToString(),
            SelectedObjectId: selectedObjectId == Guid.Empty ? string.Empty : selectedObjectId.ToString("D"),
            SelectedObjectName: ResolveInsertObjectPersistenceSelectedObjectName(context.Sheet, selectedObjectId, selectedObjectKind),
            ShapeCount: context.Sheet.DrawingShapes.Count,
            TextBoxCount: context.Sheet.TextBoxes.Count,
            PictureCount: context.Sheet.Pictures.Count,
            HyperlinkCell: context.HyperlinkCell.ToA1(),
            HyperlinkPersisted: context.Sheet.Hyperlinks.ContainsKey(context.HyperlinkCell),
            ThreadedCommentCell: context.ThreadedCommentCell.ToA1(),
            ThreadedCommentPersisted: context.Sheet.ThreadedComments.ContainsKey(context.ThreadedCommentCell),
            NoteCell: context.NoteCell.ToA1(),
            NotePersisted: context.Sheet.Comments.ContainsKey(context.NoteCell),
            PersistenceStage: persistenceStage,
            SavedWorkbookOutputFileName: context.SavedWorkbookOutputFileName,
            SavedWorkbookBytes: context.SavedWorkbookBytes,
            NativePicturePickerStatus: "blocked-foreground-guarded-not-opened",
            EvidenceSummary: evidenceSummary);
    }

    private static string ResolveInsertObjectPersistenceSelectedObjectName(
        Sheet sheet,
        Guid objectId,
        FreeX.App.UI.ObjectKind kind)
    {
        if (objectId == Guid.Empty)
            return string.Empty;

        return kind switch
        {
            FreeX.App.UI.ObjectKind.Shape => sheet.DrawingShapes.FirstOrDefault(shape => shape.Id == objectId)?.Name ?? string.Empty,
            FreeX.App.UI.ObjectKind.TextBox => sheet.TextBoxes.FirstOrDefault(textBox => textBox.Id == objectId)?.Name ?? string.Empty,
            FreeX.App.UI.ObjectKind.Picture => sheet.Pictures.FirstOrDefault(picture => picture.Id == objectId)?.Name ?? string.Empty,
            _ => string.Empty
        };
    }

    private static IReadOnlyList<InsertObjectPersistenceTourPlannedCapture> CreateInsertObjectPersistencePlannedCaptures() =>
    [
        new("seeded-context-state", "freex_insert_object_persistence_seeded_context_state.png", "captured"),
        new("selected-shape-handles", "freex_insert_object_persistence_selected_shape_handles.png", "captured"),
        new("selected-text-box-handles", "freex_insert_object_persistence_selected_text_box_handles.png", "captured"),
        new("selected-picture-handles", "freex_insert_object_persistence_selected_picture_handles.png", "captured"),
        new("native-picture-picker-foreground-proof", "", "blocked-foreground-guarded-not-opened"),
        new("saved-status", "freex_insert_object_persistence_saved_status.png", "captured"),
        new("reopened-context-state", "freex_insert_object_persistence_reopened_context_state.png", "captured"),
        new("reopened-picture-handles", "freex_insert_object_persistence_reopened_picture_handles.png", "captured")
    ];

    private static void DeleteInsertObjectPersistenceTourEvidence(string outputDir)
    {
        if (!Directory.Exists(outputDir))
            return;

        foreach (var file in Directory.EnumerateFiles(outputDir, "freex_insert_object_persistence_*.png"))
            File.Delete(file);

        DeleteIfExists(Path.Combine(outputDir, InsertObjectPersistenceTourManifestFileName));
        DeleteIfExists(Path.Combine(outputDir, InsertObjectPersistenceTourSavedWorkbookFileName));
    }

    private static void ValidateInsertObjectPersistenceTourEvidence(
        string outputDir,
        IReadOnlyList<InsertObjectPersistenceTourManifestCapture> captures,
        string savedWorkbookPath)
    {
        if (captures.Count != 7)
            throw new InvalidOperationException($"Insert object persistence tour expected 7 actual captures but created {captures.Count}.");

        var missing = captures
            .Select(capture => capture.OutputFileName)
            .Where(fileName => !File.Exists(Path.Combine(outputDir, fileName)))
            .ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException(
                $"Insert object persistence tour did not create {missing.Length} planned capture(s): {string.Join(", ", missing)}.");

        var blank = captures
            .Select(capture => capture.OutputFileName)
            .Where(fileName => !IsNonBlankPng(Path.Combine(outputDir, fileName)))
            .ToArray();
        if (blank.Length > 0)
            throw new InvalidOperationException(
                $"Insert object persistence tour created blank capture(s): {string.Join(", ", blank)}.");

        if (!File.Exists(savedWorkbookPath) || new FileInfo(savedWorkbookPath).Length <= 0)
            throw new InvalidOperationException("Insert object persistence tour did not retain a non-empty native FreeX workbook.");
    }

    private static async Task WriteInsertObjectPersistenceTourManifestAsync(
        string outputDir,
        InsertObjectPersistenceTourContext context,
        IReadOnlyList<InsertObjectPersistenceTourPlannedCapture> plannedCaptures,
        IReadOnlyList<InsertObjectPersistenceTourManifestCapture> captures)
    {
        var blockedCaptureCount = plannedCaptures.Count(planned => planned.Status.StartsWith("blocked-", StringComparison.Ordinal));
        var manifest = new InsertObjectPersistenceTourManifest(
            Tool: "FREEX_INSERT_OBJECT_PERSISTENCE_TOUR",
            EvidenceFamily: "insert-object-persistence-handles",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "insert:object-persistence-handles",
            OutputDirectory: outputDir,
            OutputNaming: "freex_insert_object_persistence_<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            CatalogIds: ["UI-CAT-INSERT-003", "UI-CAT-INSERT-003A", "UI-CMD-INSERT-008", "UI-CMD-INSERT-009", "UI-CMD-INSERT-010"],
            SheetName: context.Sheet.Name,
            SavedWorkbookPath: context.SavedWorkbookPath,
            SavedWorkbookOutputFileName: context.SavedWorkbookOutputFileName,
            SavedWorkbookBytes: context.SavedWorkbookBytes,
            PersistencePath: "SaveWorkbookToTargetAsync(.fxl native FreeX adapter) then OpenFileAsync(saved .fxl)",
            CaptureStatus: blockedCaptureCount == 0 ? "complete" : "captured-with-guarded-block",
            CaptureMode: IsScreenshotTourBackgroundRenderAllowed()
                ? "background-render-opt-in"
                : "foreground-guarded-render",
            PlannedCaptureCount: plannedCaptures.Count,
            ActualCaptureCount: captures.Count,
            BlockedCaptureCount: blockedCaptureCount,
            Pairing: new InsertObjectPersistenceTourManifestPairing(
                "interactive:insert-object-persistence:<State>",
                "excel",
                "not-yet-wired",
                "not-yet-captured"),
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 allowed deterministic in-process RenderTargetBitmap captures; no global mouse, keyboard, native file picker, UIA, or screen capture input is used."
                    : "Window captures abort unless the expected FreeX WPF surface owns foreground focus before RenderTargetBitmap capture; the native picture picker is not opened because this tour has no safe foreground-owned automation path for it."),
            PlannedCaptures: plannedCaptures,
            Captures: captures,
            SubmittedMutations:
            [
                "SetHyperlinkCommand inserts the hyperlink and display text at B2.",
                "SetThreadedCommentCommand inserts the threaded comment at C2.",
                "SetCommentCommand inserts the note at D2.",
                "AddDrawingShapeCommand inserts the rectangle at B5.",
                "AddTextBoxCommand inserts the text box at D5.",
                "PictureInsertionPlacementPlanner.CreateInsertPictureCommand creates the InsertPictureCommand placeholder at B9.",
                "GridView SelectedObjectId/SelectedObjectKind selects shape/text-box/picture objects so the rendered selection border and handles are captured.",
                "SaveWorkbookToTargetAsync writes the native .fxl workbook and OpenFileAsync reloads it through the host open path."
            ],
            CoveredStates:
            [
                "Seeded command-backed hyperlink/comment/note/object worksheet state.",
                "Shape selected with object handles before save.",
                "Text box selected with object handles before save.",
                "Picture placeholder selected with object handles before save.",
                "Saved native FreeX workbook status.",
                "Reopened native FreeX workbook context state.",
                "Reopened picture placeholder selected with object handles."
            ],
            Limitations:
            [
                "Native picture picker foreground proof is recorded as blocked/guarded; this tour does not open it because doing so safely would require foreground-owned native-dialog automation not available here.",
                "Picture evidence uses the supported InsertPictureCommand path with deterministic placeholder bytes rather than selecting a file from the native picker.",
                "Selection-handle captures set the same GridView selected-object state produced by object clicks; they do not synthesize physical mouse drag, resize, rotate, or text-edit gestures.",
                "Persistence is proven for the native FreeX .fxl adapter through host save/open services; XLSX drawing/comment/hyperlink round-trip parity remains separate.",
                "No paired Microsoft Excel screenshots are produced by this tool."
            ]);

        var path = Path.Combine(outputDir, InsertObjectPersistenceTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.InsertObjectPersistenceTourManifest);
    }

    private sealed record InsertObjectPersistenceTourContext(
        Sheet Sheet,
        DrawingShapeModel Shape,
        FreeX.Core.Model.TextBoxModel TextBox,
        PictureModel Picture,
        CellAddress HyperlinkCell,
        CellAddress ThreadedCommentCell,
        CellAddress NoteCell,
        string SavedWorkbookPath,
        string SavedWorkbookOutputFileName,
        long SavedWorkbookBytes,
        string PersistenceStage);

    private sealed record InsertObjectPersistenceTourManifest(
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
        int BlockedCaptureCount,
        InsertObjectPersistenceTourManifestPairing Pairing,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        IReadOnlyList<InsertObjectPersistenceTourPlannedCapture> PlannedCaptures,
        IReadOnlyList<InsertObjectPersistenceTourManifestCapture> Captures,
        IReadOnlyList<string> SubmittedMutations,
        IReadOnlyList<string> CoveredStates,
        IReadOnlyList<string> Limitations);

    private sealed record InsertObjectPersistenceTourManifestPairing(
        string PairKeyTemplate,
        string CounterpartApp,
        string CounterpartTool,
        string CounterpartStatus);

    private sealed record InsertObjectPersistenceTourPlannedCapture(
        string State,
        string OutputFileName,
        string Status);

    private sealed record InsertObjectPersistenceTourManifestCapture(
        string CaptureKey,
        string PairKey,
        IReadOnlyList<string> CatalogIds,
        string State,
        string FileName,
        string OutputFileName,
        string CaptureMethod,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight,
        string SheetName,
        string SelectedRange,
        string SelectedObjectKind,
        string SelectedObjectId,
        string SelectedObjectName,
        int ShapeCount,
        int TextBoxCount,
        int PictureCount,
        string HyperlinkCell,
        bool HyperlinkPersisted,
        string ThreadedCommentCell,
        bool ThreadedCommentPersisted,
        string NoteCell,
        bool NotePersisted,
        string PersistenceStage,
        string SavedWorkbookOutputFileName,
        long SavedWorkbookBytes,
        string NativePicturePickerStatus,
        string EvidenceSummary);
}
