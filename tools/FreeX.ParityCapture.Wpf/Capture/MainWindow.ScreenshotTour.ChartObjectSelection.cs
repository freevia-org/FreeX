using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private async Task CaptureChartObjectSelectionTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteChartObjectSelectionTourEvidence(outputDir);

        var savedWorkbookPath = Path.Combine(outputDir, ChartObjectSelectionTourSavedWorkbookFileName);
        DeleteIfExists(savedWorkbookPath);

        WindowState = WindowState.Normal;
        Width = 1280;
        Height = 820;
        await Task.Delay(700);

        var context = EnsureChartObjectSelectionTourContext();
        var captures = new List<ChartObjectSelectionTourManifestCapture>();
        Window? openDialog = null;

        try
        {
            SelectChartObjectSelectionChart(context);
            SelectRibbonTourTab(RibbonScreenshotTourPlanner.ChartContextTabs.Single(tab => tab.Header == "Chart Design"));
            captures.Add(await CaptureChartObjectSelectionWindowStateAsync(
                outputDir,
                context,
                "chart-selected-design-handles",
                "Selected embedded chart with Chart Design tab and object handles",
                "freex_chart_object_selection_chart_design_handles",
                "Selected chart uses GridView SelectedObjectKind.Chart and renders the blue object border, resize handles, and rotation grip while Chart Design is selected.",
                "chart-selection"));

            SelectRibbonTourTab(RibbonScreenshotTourPlanner.ChartContextTabs.Single(tab => tab.Header == "Format"));
            captures.Add(await CaptureChartObjectSelectionWindowStateAsync(
                outputDir,
                context,
                "chart-selected-format-handles",
                "Selected embedded chart with Format tab and object handles",
                "freex_chart_object_selection_chart_format_handles",
                "The same selected chart keeps object handles visible while the contextual Format tab exposes chart element formatting commands.",
                "chart-selection"));

            openDialog = new SelectDataSourceDialog(
                FormatRangeReference(context.Chart.DataRange.Start, context.Chart.DataRange.End),
                context.Chart.FirstColIsCategories,
                request => { },
                context.Sheet.Id,
                ResolveSheetIdByName)
            {
                Owner = this
            };
            await ShowChartObjectSelectionDialogAsync(openDialog);
            captures.Add(await CaptureChartObjectSelectionDialogAsync(
                outputDir,
                context,
                openDialog,
                "select-data-dialog",
                "Select Data Source dialog",
                "freex_chart_object_selection_select_data_dialog",
                "Select Data Source displays the active chart range, Switch Row/Column, first-column categories, series preview, axis labels, and Hidden and Empty Cells command surface.",
                "chart-picker"));
            CloseChartObjectSelectionDialog(openDialog);
            openDialog = null;

            openDialog = new ChangeChartTypeDialog(context.Chart.Type) { Owner = this };
            await ShowChartObjectSelectionDialogAsync(openDialog);
            captures.Add(await CaptureChartObjectSelectionDialogAsync(
                outputDir,
                context,
                openDialog,
                "change-chart-type-dialog",
                "Change Chart Type dialog",
                "freex_chart_object_selection_change_chart_type_dialog",
                "Change Chart Type opens the production family/subtype picker for the selected chart.",
                "chart-picker"));
            CloseChartObjectSelectionDialog(openDialog);
            openDialog = null;

            openDialog = new ChartStyleDialog(context.Chart) { Owner = this };
            await ShowChartObjectSelectionDialogAsync(openDialog);
            captures.Add(await CaptureChartObjectSelectionDialogAsync(
                outputDir,
                context,
                openDialog,
                "chart-styles-dialog",
                "Chart Styles dialog",
                "freex_chart_object_selection_chart_styles_dialog",
                "Chart Styles shows the selected chart's style gallery and selected style state.",
                "chart-picker"));
            CloseChartObjectSelectionDialog(openDialog);
            openDialog = null;

            openDialog = new ChartTitlesDialog(context.Chart.Title, context.Chart.XAxisTitle, context.Chart.YAxisTitle) { Owner = this };
            await ShowChartObjectSelectionDialogAsync(openDialog);
            captures.Add(await CaptureChartObjectSelectionDialogAsync(
                outputDir,
                context,
                openDialog,
                "chart-titles-dialog",
                "Chart Titles dialog",
                "freex_chart_object_selection_chart_titles_dialog",
                "Chart Titles exposes chart title, horizontal axis title, and vertical axis title fields for the selected chart.",
                "chart-picker"));
            CloseChartObjectSelectionDialog(openDialog);
            openDialog = null;

            openDialog = new ChartAreaLegendDialog(context.Chart) { Owner = this };
            await ShowChartObjectSelectionDialogAsync(openDialog);
            captures.Add(await CaptureChartObjectSelectionDialogAsync(
                outputDir,
                context,
                openDialog,
                "format-chart-area-dialog",
                "Format Chart Area dialog",
                "freex_chart_object_selection_format_chart_area_dialog",
                "Format Chart Area captures chart area, plot area, and legend formatting controls that stand in for supported advanced chart object surfaces.",
                "chart-picker"));
            CloseChartObjectSelectionDialog(openDialog);
            openDialog = null;

            SelectChartObjectSelectionShape(context);
            captures.Add(await CaptureChartObjectSelectionWindowStateAsync(
                outputDir,
                context,
                "shape-selected-handles",
                "Selected shape with object handles",
                "freex_chart_object_selection_shape_handles",
                "Seeded rectangle object is selected with object border, resize handles, and rotation grip visible on the Draw tab.",
                "object-selection"));

            var shapeMenuCapture = await CaptureChartObjectSelectionObjectContextMenuAsync(outputDir, context);
            captures.Add(shapeMenuCapture);

            SubmitChartObjectSelectionArrangeMutations(context);
            captures.Add(await CaptureChartObjectSelectionSelectionPaneAsync(outputDir, context));

            context = await SaveChartObjectSelectionWorkbookAsync(savedWorkbookPath, context);
            captures.Add(await CaptureChartObjectSelectionWindowStateAsync(
                outputDir,
                context,
                "saved-native-workbook",
                "Saved workbook with chart/object selection state visible",
                "freex_chart_object_selection_saved_native_workbook",
                "Native FreeX save completed through SaveWorkbookToTargetAsync while selected object evidence remains visible.",
                "saved"));

            await OpenFileAsync(savedWorkbookPath);
            context = ResolveChartObjectSelectionCurrentContext(savedWorkbookPath, "after-reopen");
            _options.ObjectsDisplay = AppOptionsObjectDisplay.All;
            SelectChartObjectSelectionChart(context);
            SelectRibbonTourTab(RibbonScreenshotTourPlanner.ChartContextTabs.Single(tab => tab.Header == "Chart Design"));
            captures.Add(await CaptureChartObjectSelectionWindowStateAsync(
                outputDir,
                context,
                "reopened-chart-selected-handles",
                "Reopened selected chart with handles",
                "freex_chart_object_selection_reopened_chart_handles",
                "Saved native FreeX workbook was reopened through OpenFileAsync; the persisted chart can be selected again with object handles and contextual tabs visible.",
                "after-reopen"));

            ValidateChartObjectSelectionTourEvidence(outputDir, captures, savedWorkbookPath);
            await WriteChartObjectSelectionTourManifestAsync(outputDir, context, captures);
        }
        catch
        {
            DeleteChartObjectSelectionTourEvidence(outputDir);
            throw;
        }
        finally
        {
            if (openDialog is { IsVisible: true })
                CloseChartObjectSelectionDialog(openDialog);

            if (SheetGrid?.ContextMenu is { IsOpen: true } menu)
                menu.IsOpen = false;
        }
    }

    private ChartObjectSelectionTourContext EnsureChartObjectSelectionTourContext()
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("Chart/object selection tour requires an active worksheet.");

        HideStartScreen();
        _currentSheetId = sheet.Id;
        _currentFilePath = null;
        _workbook.Name = "Chart Object Selection";
        sheet.Name = "Chart Object Selection";
        _options.ObjectsDisplay = AppOptionsObjectDisplay.All;

        for (uint row = 1; row <= 18; row++)
        {
            for (uint col = 1; col <= 10; col++)
            {
                var address = new CellAddress(sheet.Id, row, col);
                sheet.ClearCell(address);
                sheet.Comments.Remove(address);
                sheet.ThreadedComments.Remove(address);
                sheet.Hyperlinks.Remove(address);
                sheet.HyperlinkMetadata.Remove(address);
            }
        }

        sheet.Charts.Clear();
        sheet.Sparklines.Clear();
        sheet.DrawingShapes.Clear();
        sheet.Pictures.Clear();
        sheet.TextBoxes.Clear();
        sheet.DrawingObjectZOrder.Clear();
        sheet.StructuredTables.RemoveAll(table => string.Equals(table.Name, ScreenshotTourTableName, StringComparison.OrdinalIgnoreCase));
        sheet.PivotTables.RemoveAll(pivot => string.Equals(pivot.Name, ScreenshotTourPivotTableName, StringComparison.OrdinalIgnoreCase));

        SeedChartObjectSelectionSourceData(sheet);
        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 4));
        ExecuteChartObjectSelectionCommand(
            new AddChartCommand(
                sheet.Id,
                sourceRange,
                ChartType.Column,
                ScreenshotTourChartName,
                left: 420,
                top: 120,
                width: 610,
                height: 360),
            "Insert Chart");

        var chart = FindScreenshotTourChart(sheet)
            ?? throw new InvalidOperationException("Chart/object selection tour could not find the seeded chart.");
        chart.Name = "Selection Evidence Chart";
        ExecuteChartObjectSelectionCommand(
            new SetChartLayoutCommand(
                sheet.Id,
                chart.Id,
                new ChartLayoutOptions(
                    Title: "Object Selection Revenue",
                    XAxisTitle: "Month",
                    YAxisTitle: "Revenue",
                    ShowLegend: true,
                    LegendPosition: ChartLegendPosition.Right,
                    ShowDataLabels: true,
                    DataLabelPosition: ChartDataLabelPosition.OutsideEnd,
                    ShowDataLabelValue: true,
                    DataLabelNumberFormat: ChartDataLabelNumberFormat.Number,
                    ChartAreaFillColor: new CellColor(245, 247, 250),
                    PlotAreaFillColor: new CellColor(255, 255, 255),
                    PlotAreaBorderColor: new CellColor(71, 85, 105),
                    PlotAreaBorderThickness: 1.25)),
            "Format Chart Layout");
        ExecuteChartObjectSelectionCommand(
            new SetChartStyleCommand(sheet.Id, chart.Id, 13),
            "Set Chart Style");

        var shape = new DrawingShapeModel
        {
            Id = Guid.Parse("d1111111-4444-4444-8444-d11111111111"),
            Anchor = new CellAddress(sheet.Id, 9, 2),
            Kind = DrawingShapeKind.Rectangle,
            Width = 190,
            Height = 92,
            Name = "Selection Rectangle",
            Title = "Selection rectangle",
            AltText = "Rectangle object for chart/object selection evidence.",
            FillColor = new CellColor(91, 155, 213),
            OutlineColor = new CellColor(31, 78, 121),
            RotationDegrees = 8
        };
        sheet.DrawingShapes.Add(shape);

        var picture = new PictureModel
        {
            Id = Guid.Parse("d2222222-5555-4555-8555-d22222222222"),
            Anchor = new CellAddress(sheet.Id, 9, 5),
            Kind = PictureKind.Image,
            ImageBytes = [1, 2, 3, 4],
            ContentType = "image/png",
            Name = "Selection Picture",
            AltText = "Picture placeholder for chart/object selection evidence.",
            Width = 176,
            Height = 98
        };
        sheet.Pictures.Add(picture);

        var textBox = new FreeX.Core.Model.TextBoxModel
        {
            Id = Guid.Parse("d3333333-6666-4666-8666-d33333333333"),
            Anchor = new CellAddress(sheet.Id, 13, 3),
            Text = "Selection pane object",
            Width = 230,
            Height = 78,
            Name = "Selection Text Box",
            AltText = "Text box object for chart/object selection evidence.",
            FillColor = new CellColor(226, 239, 218),
            OutlineColor = new CellColor(84, 130, 53)
        };
        sheet.TextBoxes.Add(textBox);

        sheet.DrawingObjectZOrder.Add(new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Shape, shape.Id));
        sheet.DrawingObjectZOrder.Add(new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Picture, picture.Id));
        sheet.DrawingObjectZOrder.Add(new DrawingObjectZOrderEntry(SelectionPaneObjectKind.TextBox, textBox.Id));

        RefreshChartContextualTabs();
        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
        MarkWorkbookDirty();
        UpdateTitleBar();

        return new ChartObjectSelectionTourContext(
            Sheet: sheet,
            Chart: chart,
            Shape: shape,
            Picture: picture,
            TextBox: textBox,
            SourceRange: sourceRange,
            SavedWorkbookPath: string.Empty,
            SavedWorkbookOutputFileName: string.Empty,
            SavedWorkbookBytes: 0,
            PersistenceStage: "seeded");
    }

    private static void SeedChartObjectSelectionSourceData(Sheet sheet)
    {
        var cells = new (uint Row, uint Col, ScalarValue Value)[]
        {
            (1, 1, new TextValue("Month")),
            (1, 2, new TextValue("North")),
            (1, 3, new TextValue("South")),
            (1, 4, new TextValue("East")),
            (2, 1, new TextValue("Jan")),
            (2, 2, new NumberValue(1280)),
            (2, 3, new NumberValue(940)),
            (2, 4, new NumberValue(760)),
            (3, 1, new TextValue("Feb")),
            (3, 2, new NumberValue(1460)),
            (3, 3, new NumberValue(1020)),
            (3, 4, new NumberValue(890)),
            (4, 1, new TextValue("Mar")),
            (4, 2, new NumberValue(1325)),
            (4, 3, new NumberValue(1180)),
            (4, 4, new NumberValue(940)),
            (5, 1, new TextValue("Apr")),
            (5, 2, new NumberValue(1580)),
            (5, 3, new NumberValue(1210)),
            (5, 4, new NumberValue(1035)),
            (6, 1, new TextValue("May")),
            (6, 2, new NumberValue(1710)),
            (6, 3, new NumberValue(1325)),
            (6, 4, new NumberValue(1110)),
            (8, 1, new TextValue("Objects")),
            (12, 1, new TextValue("Selection Pane"))
        };

        foreach (var (row, col, value) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, row, col), value);
    }

    private void ExecuteChartObjectSelectionCommand(IWorkbookCommand command, string label)
    {
        if (!TryExecuteCommand(command, label, out var outcome))
            throw new InvalidOperationException(outcome.ErrorMessage ?? $"Chart/object selection tour command '{label}' failed.");
    }

    private void SelectChartObjectSelectionChart(ChartObjectSelectionTourContext context)
    {
        SetActiveCell(context.SourceRange.Start);
        EnsureCellVisible(context.SourceRange.Start);
        if (SheetGrid is not null)
        {
            SheetGrid.SelectedRange = new GridRange(context.SourceRange.Start, context.SourceRange.Start);
            SheetGrid.SelectedRanges = null;
            SheetGrid.SelectedObjectId = context.Chart.Id;
            SheetGrid.SelectedObjectKind = FreeX.App.UI.ObjectKind.Chart;
            SheetGrid.InvalidateVisual();
        }

        RefreshChartContextualTabs();
        UpdateViewport();
        RefreshToolbar();
    }

    private void SelectChartObjectSelectionShape(ChartObjectSelectionTourContext context)
    {
        SelectDrawObjectFormattingObject(context.Shape.Anchor, context.Shape.Id, FreeX.App.UI.ObjectKind.Shape);
        SelectRibbonTourTab(RibbonScreenshotTourPlanner.DefaultTabs.Single(tab => tab.Header == "Draw"));
        UpdateViewport();
        RefreshToolbar();
    }

    private async Task<ChartObjectSelectionTourManifestCapture> CaptureChartObjectSelectionWindowStateAsync(
        string outputDir,
        ChartObjectSelectionTourContext context,
        string state,
        string surface,
        string fileName,
        string evidenceSummary,
        string persistenceStage)
    {
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await CaptureCurrentWindowAsync(outputDir, fileName, 820);
        return CreateChartObjectSelectionCapture(
            context,
            state,
            surface,
            fileName,
            "RenderTargetBitmap-window-full",
            ActualWidth,
            Math.Min(ActualHeight, 820),
            evidenceSummary,
            persistenceStage,
            []);
    }

    private static async Task ShowChartObjectSelectionDialogAsync(Window dialog)
    {
        dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        dialog.Show();
        dialog.Activate();
        dialog.UpdateLayout();
        await Task.Delay(400);
        await dialog.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
    }

    private static void CloseChartObjectSelectionDialog(Window dialog)
    {
        if (dialog.IsVisible)
            dialog.Close();
    }

    private async Task<ChartObjectSelectionTourManifestCapture> CaptureChartObjectSelectionDialogAsync(
        string outputDir,
        ChartObjectSelectionTourContext context,
        Window dialog,
        string state,
        string surface,
        string fileName,
        string evidenceSummary,
        string persistenceStage)
    {
        dialog.UpdateLayout();
        await Task.Delay(200);
        await dialog.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        await CaptureWindowElementForScreenshotTourAsync(dialog, outputDir, fileName);
        return CreateChartObjectSelectionCapture(
            context,
            state,
            surface,
            fileName,
            "RenderTargetBitmap-dialog-window",
            dialog.ActualWidth,
            dialog.ActualHeight,
            evidenceSummary,
            persistenceStage,
            []);
    }

    private async Task<ChartObjectSelectionTourManifestCapture> CaptureChartObjectSelectionObjectContextMenuAsync(
        string outputDir,
        ChartObjectSelectionTourContext context)
    {
        OnGridContextMenuRequested(context.Shape.Anchor, GetKeyboardContextMenuGridPoint(context.Shape.Anchor));
        await Task.Delay(350);

        if (SheetGrid.ContextMenu is not { } menu)
            throw new InvalidOperationException("Chart/object selection tour could not open the selected shape context menu.");

        try
        {
            menu.UpdateLayout();
            await CaptureElementAsync(menu, outputDir, "freex_chart_object_selection_shape_context_menu");
            var headers = ReadChartObjectSelectionMenuHeaders(menu);
            return CreateChartObjectSelectionCapture(
                context,
                "shape-context-menu",
                "Selected shape context menu",
                "freex_chart_object_selection_shape_context_menu",
                "RenderTargetBitmap-object-context-menu",
                menu.ActualWidth,
                menu.ActualHeight,
                $"Selected shape context menu exposes object formatting, Size and Properties, rotate, fill, outline, alt text, Selection Pane, and arrange commands: {string.Join(", ", headers)}.",
                "object-selection",
                headers);
        }
        finally
        {
            menu.IsOpen = false;
        }
    }

    private static IReadOnlyList<string> ReadChartObjectSelectionMenuHeaders(ContextMenu menu) =>
        menu.Items
            .OfType<MenuItem>()
            .Select(item => item.Header?.ToString()?.Replace("_", string.Empty, StringComparison.Ordinal) ?? string.Empty)
            .Where(header => !string.IsNullOrWhiteSpace(header))
            .ToArray();

    private void SubmitChartObjectSelectionArrangeMutations(ChartObjectSelectionTourContext context)
    {
        ExecuteChartObjectSelectionCommand(
            new RenameSelectionPaneObjectCommand(context.Sheet.Id, SelectionPaneObjectKind.Shape, context.Shape.Id, "Selected Evidence Shape"),
            "Selection Pane Rename Shape");
        ExecuteChartObjectSelectionCommand(
            new SetSelectionPaneObjectVisibilityCommand(context.Sheet.Id, SelectionPaneObjectKind.Picture, context.Picture.Id, isVisible: false),
            "Selection Pane Hide Picture");
        ExecuteChartObjectSelectionCommand(
            new MoveSelectionPaneObjectCommand(context.Sheet.Id, SelectionPaneObjectKind.TextBox, context.TextBox.Id, forward: true),
            "Selection Pane Bring Text Box Forward");
    }

    private async Task<ChartObjectSelectionTourManifestCapture> CaptureChartObjectSelectionSelectionPaneAsync(
        string outputDir,
        ChartObjectSelectionTourContext context)
    {
        var dialog = new SelectionPaneDialog(SelectionPaneDialog.BuildItems(context.Sheet)) { Owner = this };
        try
        {
            dialog.Show();
            dialog.Activate();
            await Task.Delay(350);
            dialog.UpdateLayout();
            await CaptureWindowElementForScreenshotTourAsync(dialog, outputDir, "freex_chart_object_selection_selection_pane_arranged");
            return CreateChartObjectSelectionCapture(
                context,
                "selection-pane-arranged",
                "Selection Pane after object arrange mutations",
                "freex_chart_object_selection_selection_pane_arranged",
                "RenderTargetBitmap-selection-pane-dialog-window",
                dialog.ActualWidth,
                dialog.ActualHeight,
                "Selection Pane reflects submitted rename, hidden-picture visibility state, and text-box bring-forward order for selected objects.",
                "object-selection",
                SelectionPaneDialog.BuildItems(context.Sheet).Select(item => $"{item.Kind}:{item.Name}:{item.IsVisible}").ToArray());
        }
        finally
        {
            dialog.Close();
        }
    }

    private async Task<ChartObjectSelectionTourContext> SaveChartObjectSelectionWorkbookAsync(
        string savedWorkbookPath,
        ChartObjectSelectionTourContext context)
    {
        var adapter = FileFormatResolver.FindSaveAdapter(_fileAdapters, ".fxl", out _)
            ?? throw new InvalidOperationException("Chart/object selection tour could not find the native FreeX save adapter.");
        if (!await SaveWorkbookToTargetAsync(new FileSaveTarget(savedWorkbookPath, adapter)))
            throw new InvalidOperationException("Chart/object selection tour could not save the native FreeX workbook.");

        return context with
        {
            SavedWorkbookPath = savedWorkbookPath,
            SavedWorkbookOutputFileName = Path.GetFileName(savedWorkbookPath),
            SavedWorkbookBytes = new FileInfo(savedWorkbookPath).Length,
            PersistenceStage = "saved"
        };
    }

    private ChartObjectSelectionTourContext ResolveChartObjectSelectionCurrentContext(
        string savedWorkbookPath,
        string persistenceStage)
    {
        var sheet = _workbook.Sheets.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, "Chart Object Selection", StringComparison.OrdinalIgnoreCase))
            ?? GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("Chart/object selection tour could not resolve the reopened worksheet.");
        _currentSheetId = sheet.Id;

        var chart = sheet.Charts.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, "Selection Evidence Chart", StringComparison.OrdinalIgnoreCase))
            ?? sheet.Charts.FirstOrDefault()
            ?? throw new InvalidOperationException("Chart/object selection tour could not resolve the persisted chart.");
        var shape = sheet.DrawingShapes.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, "Selected Evidence Shape", StringComparison.OrdinalIgnoreCase))
            ?? sheet.DrawingShapes.FirstOrDefault()
            ?? throw new InvalidOperationException("Chart/object selection tour could not resolve the persisted shape.");
        var picture = sheet.Pictures.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, "Selection Picture", StringComparison.OrdinalIgnoreCase))
            ?? sheet.Pictures.FirstOrDefault()
            ?? throw new InvalidOperationException("Chart/object selection tour could not resolve the persisted picture.");
        var textBox = sheet.TextBoxes.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, "Selection Text Box", StringComparison.OrdinalIgnoreCase))
            ?? sheet.TextBoxes.FirstOrDefault()
            ?? throw new InvalidOperationException("Chart/object selection tour could not resolve the persisted text box.");

        return new ChartObjectSelectionTourContext(
            Sheet: sheet,
            Chart: chart,
            Shape: shape,
            Picture: picture,
            TextBox: textBox,
            SourceRange: new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 4)),
            SavedWorkbookPath: savedWorkbookPath,
            SavedWorkbookOutputFileName: Path.GetFileName(savedWorkbookPath),
            SavedWorkbookBytes: File.Exists(savedWorkbookPath) ? new FileInfo(savedWorkbookPath).Length : 0,
            PersistenceStage: persistenceStage);
    }

    private ChartObjectSelectionTourManifestCapture CreateChartObjectSelectionCapture(
        ChartObjectSelectionTourContext context,
        string state,
        string surface,
        string fileName,
        string captureMethod,
        double captureLogicalWidth,
        double captureLogicalHeight,
        string evidenceSummary,
        string persistenceStage,
        IReadOnlyList<string> visibleCommands)
    {
        var selectedObjectId = SheetGrid?.SelectedObjectId ?? Guid.Empty;
        var selectedObjectKind = SheetGrid?.SelectedObjectKind ?? FreeX.App.UI.ObjectKind.None;
        return new ChartObjectSelectionTourManifestCapture(
            CaptureKey: $"chart-object-selection:{state}",
            PairKey: $"interactive:chart-object-selection:{state}",
            CatalogIds:
            [
                "UI-CAT-INSERT-002",
                "UI-CAT-INSERT-002B",
                "UI-CAT-INSERT-002C",
                "UI-CAT-DRAW-001",
                "UI-CAT-DRAW-001A",
                "UI-CAT-DRAW-001C",
                "UI-CAT-CONTEXT-003"
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
            SelectedObjectKind: selectedObjectKind.ToString(),
            SelectedObjectId: selectedObjectId == Guid.Empty ? string.Empty : selectedObjectId.ToString("D"),
            SelectedObjectName: ResolveChartObjectSelectionSelectedObjectName(context.Sheet, selectedObjectId, selectedObjectKind),
            ChartSnapshot: DescribeChartObjectSelectionChart(context.Chart),
            ShapeSnapshot: DescribeShape(context.Shape),
            PictureSnapshot: DescribePicture(context.Picture),
            TextBoxSnapshot: DescribeTextBox(context.TextBox),
            DrawingZOrder: context.Sheet.DrawingObjectZOrder.Select(entry => $"{entry.Kind}:{entry.Id:N}").ToArray(),
            PersistenceStage: persistenceStage,
            SavedWorkbookOutputFileName: context.SavedWorkbookOutputFileName,
            SavedWorkbookBytes: context.SavedWorkbookBytes,
            VisibleCommands: visibleCommands,
            EvidenceSummary: evidenceSummary);
    }

    private static string ResolveChartObjectSelectionSelectedObjectName(
        Sheet sheet,
        Guid objectId,
        FreeX.App.UI.ObjectKind kind)
    {
        if (objectId == Guid.Empty)
            return string.Empty;

        return kind switch
        {
            FreeX.App.UI.ObjectKind.Chart => sheet.Charts.FirstOrDefault(chart => chart.Id == objectId)?.Name ?? string.Empty,
            FreeX.App.UI.ObjectKind.Shape => sheet.DrawingShapes.FirstOrDefault(shape => shape.Id == objectId)?.Name ?? string.Empty,
            FreeX.App.UI.ObjectKind.Picture => sheet.Pictures.FirstOrDefault(picture => picture.Id == objectId)?.Name ?? string.Empty,
            FreeX.App.UI.ObjectKind.TextBox => sheet.TextBoxes.FirstOrDefault(textBox => textBox.Id == objectId)?.Name ?? string.Empty,
            _ => string.Empty
        };
    }

    private static string DescribeChartObjectSelectionChart(ChartModel chart) =>
        string.Join(
            "|",
            $"name={chart.Name}",
            $"title={chart.Title}",
            $"type={chart.Type}",
            $"range={chart.DataRange}",
            $"bounds={chart.Left:0.#},{chart.Top:0.#},{chart.Width:0.#}x{chart.Height:0.#}",
            $"legend={chart.ShowLegend}:{chart.LegendPosition}",
            $"xAxis={chart.XAxisTitle}",
            $"yAxis={chart.YAxisTitle}");

    private static void DeleteChartObjectSelectionTourEvidence(string outputDir)
    {
        if (!Directory.Exists(outputDir))
            return;

        foreach (var file in Directory.EnumerateFiles(outputDir, "freex_chart_object_selection_*.png"))
            File.Delete(file);

        DeleteIfExists(Path.Combine(outputDir, ChartObjectSelectionTourManifestFileName));
        DeleteIfExists(Path.Combine(outputDir, ChartObjectSelectionTourSavedWorkbookFileName));
    }

    private static void ValidateChartObjectSelectionTourEvidence(
        string outputDir,
        IReadOnlyList<ChartObjectSelectionTourManifestCapture> captures,
        string savedWorkbookPath)
    {
        if (captures.Count != 12)
            throw new InvalidOperationException($"Chart/object selection tour expected 12 captures but created {captures.Count}.");

        var missing = captures
            .Select(capture => capture.OutputFileName)
            .Where(fileName => !File.Exists(Path.Combine(outputDir, fileName)))
            .ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException(
                $"Chart/object selection tour did not create {missing.Length} planned capture(s): {string.Join(", ", missing)}.");

        var blank = captures
            .Select(capture => capture.OutputFileName)
            .Where(fileName => !IsNonBlankPng(Path.Combine(outputDir, fileName)))
            .ToArray();
        if (blank.Length > 0)
            throw new InvalidOperationException(
                $"Chart/object selection tour created blank capture(s): {string.Join(", ", blank)}.");

        if (!File.Exists(savedWorkbookPath) || new FileInfo(savedWorkbookPath).Length <= 0)
            throw new InvalidOperationException("Chart/object selection tour did not retain a non-empty native FreeX workbook.");
    }

    private static async Task WriteChartObjectSelectionTourManifestAsync(
        string outputDir,
        ChartObjectSelectionTourContext context,
        IReadOnlyList<ChartObjectSelectionTourManifestCapture> captures)
    {
        var manifest = new ChartObjectSelectionTourManifest(
            Tool: "FREEX_CHART_OBJECT_SELECTION_TOUR",
            EvidenceFamily: "chart-object-selection-advanced-pickers",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "chart-object-selection:visual-evidence",
            OutputDirectory: outputDir,
            OutputNaming: "freex_chart_object_selection_<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            CatalogIds:
            [
                "UI-CAT-INSERT-002",
                "UI-CAT-INSERT-002B",
                "UI-CAT-INSERT-002C",
                "UI-CAT-DRAW-001",
                "UI-CAT-DRAW-001A",
                "UI-CAT-DRAW-001C",
                "UI-CAT-CONTEXT-003"
            ],
            SheetName: context.Sheet.Name,
            SourceRange: context.SourceRange.ToString(),
            ChartTitle: context.Chart.Title ?? string.Empty,
            ChartType: context.Chart.Type.ToString(),
            ChartDataRange: context.Chart.DataRange.ToString(),
            SavedWorkbookPath: context.SavedWorkbookPath,
            SavedWorkbookOutputFileName: context.SavedWorkbookOutputFileName,
            SavedWorkbookBytes: context.SavedWorkbookBytes,
            PersistencePath: "SaveWorkbookToTargetAsync(.fxl native FreeX adapter) then OpenFileAsync(saved .fxl)",
            CaptureStatus: "captured-with-hit-test-gaps",
            CaptureMode: IsScreenshotTourBackgroundRenderAllowed()
                ? "background-render-opt-in"
                : "foreground-guarded-render",
            PlannedCaptureCount: captures.Count,
            ActualCaptureCount: captures.Count,
            Pairing: new ChartObjectSelectionTourManifestPairing(
                "interactive:chart-object-selection:<State>",
                "excel",
                "not-yet-wired",
                "not-yet-captured"),
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 allowed deterministic in-process RenderTargetBitmap captures; no global mouse, keyboard, range-picker, UIA, drag-handle, or screen capture input is used."
                    : "Window, dialog, and menu captures abort unless the expected FreeX WPF surface owns foreground focus immediately before render and file write."),
            Captures: captures,
            CoveredStates:
            [
                "Embedded chart selected with object border, resize handles, rotation grip, and Chart Design contextual tab.",
                "Embedded chart selected with object handles and Format contextual tab.",
                "Select Data Source, Change Chart Type, Chart Styles, Chart Titles, and Format Chart Area/Legend advanced chart picker surfaces.",
                "Shape selected with object handles.",
                "Selected shape context menu with format, size, rotate, fill, outline, alt text, Selection Pane, and arrange commands.",
                "Selection Pane after rename, visibility, and z-order mutations.",
                "Native FreeX .fxl save and reopen proof with chart selectable again after reload."
            ],
            HitTestOnlyGaps:
            [
                "Chart area, plot area, series, point, axis, title, and legend subtarget selection are not captured as physical hit-test proof because FreeX currently exposes only whole-chart object selection plus the existing Waterfall point context menu route.",
                "Chart object context menus for area/plot/series/axis/title/legend are not generally available in the worksheet context menu planner; unsupported subtargets are recorded here rather than represented by synthetic evidence.",
                "Dialog OK/access-key submissions, range-picker collapse/restore, physical mouse selection, resize/rotate drag handles, UIA invoke patterns, and paired Microsoft Excel screenshots remain outside this RenderTargetBitmap tour.",
                "Persistence is proven with the native FreeX .fxl adapter; XLSX chart/object mutation persistence is a separate compatibility lane."
            ],
            Limitations:
            [
                "The selected chart/object states are set through the same GridView SelectedObjectId/SelectedObjectKind state used by object clicks, not by foreground mouse input.",
                "Advanced chart dialogs are production WPF surfaces opened directly for deterministic evidence; this tour does not submit their OK buttons.",
                "Object arrange proof uses command-bus Selection Pane mutations and captures the resulting dialog state.",
                "No paired Microsoft Excel screenshots are produced by this tool."
            ]);

        var path = Path.Combine(outputDir, ChartObjectSelectionTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.ChartObjectSelectionTourManifest);
    }

    private sealed record ChartObjectSelectionTourContext(
        Sheet Sheet,
        ChartModel Chart,
        DrawingShapeModel Shape,
        PictureModel Picture,
        FreeX.Core.Model.TextBoxModel TextBox,
        GridRange SourceRange,
        string SavedWorkbookPath,
        string SavedWorkbookOutputFileName,
        long SavedWorkbookBytes,
        string PersistenceStage);

    private sealed record ChartObjectSelectionTourManifest(
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
        string SourceRange,
        string ChartTitle,
        string ChartType,
        string ChartDataRange,
        string SavedWorkbookPath,
        string SavedWorkbookOutputFileName,
        long SavedWorkbookBytes,
        string PersistencePath,
        string CaptureStatus,
        string CaptureMode,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        ChartObjectSelectionTourManifestPairing Pairing,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        IReadOnlyList<ChartObjectSelectionTourManifestCapture> Captures,
        IReadOnlyList<string> CoveredStates,
        IReadOnlyList<string> HitTestOnlyGaps,
        IReadOnlyList<string> Limitations);

    private sealed record ChartObjectSelectionTourManifestPairing(
        string PairKeyTemplate,
        string CounterpartApp,
        string CounterpartTool,
        string CounterpartStatus);

    private sealed record ChartObjectSelectionTourManifestCapture(
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
        string SelectedObjectId,
        string SelectedObjectName,
        string ChartSnapshot,
        string ShapeSnapshot,
        string PictureSnapshot,
        string TextBoxSnapshot,
        IReadOnlyList<string> DrawingZOrder,
        string PersistenceStage,
        string SavedWorkbookOutputFileName,
        long SavedWorkbookBytes,
        IReadOnlyList<string> VisibleCommands,
        string EvidenceSummary);
}
