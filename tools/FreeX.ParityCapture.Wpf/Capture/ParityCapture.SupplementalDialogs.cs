using System.Globalization;
using System.Windows;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Charts.Editing;
using FreeX.App.Presentation.Comments;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.CustomViews;
using FreeX.App.Presentation.DefinedNames;
using FreeX.App.Presentation.Dialogs;
using FreeX.App.Presentation.Editing;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.PivotUI;
using FreeX.App.Presentation.Shell;
using FreeX.App.Presentation.SheetUI;
using FreeX.App.Presentation.TableUI;
using FreeX.App.Services;
using FreeX.App.Services.Ribbon;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

/// <summary>Returns default values for every call, so a window stand-in never touches real UI.</summary>
public class InertWorkbookWindowProxy : System.Reflection.DispatchProxy
{
    protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args) =>
        targetMethod?.ReturnType is { IsValueType: true } valueType && valueType != typeof(void)
            ? Activator.CreateInstance(valueType)
            : null;
}

/// <summary>
/// WPF side of the dialog surfaces the Avalonia tour captures through its supplemental routes
/// (<c>ResolveSupplementalParityDialogOpener</c>). Every surface id here is the exact id the Avalonia
/// capture writes, and every dialog is seeded from the same parity workbook and the same fixture values
/// (the Avalonia <c>EnsureParityChart</c> / <c>EnsureParityPivot</c> / <c>EnsureParityPicture</c> state), so
/// the comparison runner pairs them one-for-one and any remaining diff is a real rendering difference.
/// </summary>
internal static partial class ParityCapture
{
    /// <summary>One supplemental surface: its id and a factory that builds the WPF window to render.</summary>
    private sealed record SupplementalDialog(string SurfaceId, Func<Window> Create, string Note = "");

    private static IReadOnlyList<SupplementalDialog> BuildSupplementalDialogs() =>
    [
        new("dialog.ActivateSheet", () =>
        {
            var wb = ParityDemoWorkbookFactory.Create();
            return new ActivateSheetDialog(wb, wb.Sheets.Single().Id);
        }),
        new("dialog.Bookmark", () => new BookmarkDialog("A1")),
        new("dialog.CellShift", () => new CellShiftDialog(CellShiftDialogMode.Insert)),
        new("dialog.ChartAxisFormat", () => new ChartAxisFormatDialog(CreateParityChart(ChartType.Column), useXAxis: true)),
        new("dialog.ChartBarFormat", () => new ChartBarFormatDialog(CreateParityChart(ChartType.Bar))),
        new("dialog.ChartBubbleFormat", () => new ChartBubbleFormatDialog(CreateParityChart(ChartType.Bubble))),
        new("dialog.ChartDataLabels", () => new ChartDataLabelsDialog(CreateParityChart(ChartType.Column))),
        new("dialog.ChartErrorBars", () => new ChartErrorBarsDialog(CreateParityChart(ChartType.Column))),
        new("dialog.ChartPieFormat", () => new ChartPieFormatDialog(CreateParityChart(ChartType.Pie))),
        new("dialog.ChartSeriesFormat", () =>
        {
            var chart = CreateParityChart(ChartType.Column);
            return new ChartSeriesFormatDialog(chart, ChartSeriesFormatPlanner.GetSeriesCount(chart));
        }),
        new("dialog.ChartStockFormat", () => new ChartStockFormatDialog(CreateParityChart(ChartType.Stock))),
        new("dialog.ChartStyle", () => new ChartStyleDialog(CreateParityChart(ChartType.Column))),
        new("dialog.ChartTitles", () =>
        {
            var chart = CreateParityChart(ChartType.Column);
            return new ChartTitlesDialog(chart.Title, chart.XAxisTitle, chart.YAxisTitle);
        }),
        new("dialog.ChartTrendlineOptions", () => new ChartTrendlineOptionsDialog(CreateParityChart(ChartType.Column))),
        new("dialog.ColorPicker", () =>
            new ColorPickerDialog(new CellColor(91, 155, 213), allowNoColor: false, noColorButtonText: null, theme: WorkbookTheme.Office)
            {
                Title = "More Colors",
            }),
        new("dialog.ColorScaleRule", () => new ColorScaleRuleDialog(ParityRuleRange())),
        new("dialog.ColumnWidth", () => new ColumnWidthDialog(ParityColumnWidth())),
        new("dialog.CommentList", CreateParityCommentList),
        new("dialog.ConditionalFormatThreshold", () => new ConditionalFormatThresholdDialog("100")),
        new("dialog.CreateNamesFromSelection", () =>
        {
            var wb = ParityDemoWorkbookFactory.Create();
            var sheet = wb.Sheets.Single();
            var a1 = new CellAddress(sheet.Id, 1, 1);
            return new CreateNamesFromSelectionDialog(
                CreateNamesFromSelectionPlanner.DetectOptions(new GridRange(a1, a1), sheet.GetValue));
        }),
        new("dialog.CustomViewName", () =>
            new CustomViewNameDialog(CustomViewsPlanner.SuggestDefaultName(0, UiText.Get("CustomViews_DefaultName")))),
        new("dialog.DataBarRule", () => new DataBarRuleDialog(ParityRuleRange())),
        new("dialog.FillSeriesStep", () => new FillSeriesStepDialog(1)),
        new("dialog.FormatPicture", () => new FormatPictureDialog(CreateParityPicture())),
        new("dialog.FunctionArguments", () =>
        {
            var function = InsertFunctionCatalogPlanner.BuildCatalog()
                .First(entry => string.Equals(entry.Name, "SUM", StringComparison.Ordinal));
            return new FunctionArgumentsDialog(function);
        }),
        new("dialog.HeaderFooterPictureFormat", () =>
            new HeaderFooterPictureFormatDialog(new WorksheetHeaderFooterPicture(
                [],
                "image/png",
                "QuarterlyHeader.png",
                Width: 160,
                Height: 80))),
        new("dialog.HighlightCellsRule", () =>
            new HighlightCellsRuleDialog(ConditionalFormatDialogCatalog.GreaterThanRule, ParityRuleRange())),
        new("dialog.IconSetRule", () => new IconSetRuleDialog(ParityRuleRange())),
        // The Avalonia capture opens the picker with no recommendation list (the default gallery).
        new("dialog.InsertChart", () => new InsertChartDialog()),
        new("dialog.InsertFunction", () => new InsertFunctionDialog()),
        // Same entries the Avalonia ShowMergeCellsWarningParityDialogAsync plan carries (North, Widget).
        new("dialog.MergeCellsContentWarning", () => new MergeCellsContentWarningDialog(["North", "Widget"])),
        new("dialog.MoveChart", () => new MoveChartDialog(ParityDemoWorkbookFactory.SheetName)),
        new("dialog.MoveOrCopySheet", () =>
        {
            var wb = ParityDemoWorkbookFactory.Create();
            return new MoveOrCopySheetDialog(wb, wb.Sheets.Single().Id);
        }),
        new("dialog.MovePivotTable", () =>
        {
            var (wb, sheet, pivot) = CreateParityPivot();
            var destination = new GridRange(pivot.TargetRange.Start, pivot.TargetRange.Start);
            return new MovePivotTableDialog(
                PivotCreatePlanner.FormatRange(wb, sheet.Id, destination),
                sheetId: sheet.Id,
                resolveSheetId: ResolveSheetId(wb));
        }),
        new("dialog.NameDefinition", () =>
        {
            var wb = ParityDemoWorkbookFactory.Create();
            var sheet = wb.Sheets.Single();
            var a1 = new CellAddress(sheet.Id, 1, 1);
            var session = new DefinedNamesSession(wb, sheet.Id);
            return new NameDefinitionDialog(
                new NameDefinitionDialogResult(
                    "",
                    DefinedNameScope.WorkbookLabel,
                    "",
                    PivotCreatePlanner.FormatRange(wb, sheet.Id, new GridRange(a1, a1))),
                DefinedNameUiPolicy.BuildScopeOptions(session.ScopeChoices),
                isValidRange: rangeText => session.ValidateRefersTo(rangeText).IsValid,
                validateName: (name, _) => wb.ValidateNamedRangeName(name));
        }),
        new("dialog.NamedRange", () =>
        {
            var wb = ParityDemoWorkbookFactory.Create();
            var sheet = wb.Sheets.Single();
            var a1 = new CellAddress(sheet.Id, 1, 1);
            var bus = new CommandBus(_ => new WorkbookCommandContext(wb));
            return new NamedRangeDialog(wb, command => bus.Execute(wb.Id, command), new GridRange(a1, a1));
        }),
        new("dialog.ObjectSize", () =>
            new ObjectSizeDialog(150, 90, UiText.Get("MainWindowMessage_ObjectSizeTitle"))),
        // Avalonia surface dialog.OutlineGroup opens Outline Settings (ShowOutlineSettingsDialogAsync), whose
        // WPF counterpart is OutlineSettingsDialog -- not OutlineGroupDialog, the Group/Ungroup picker.
        // A never-configured sheet resolves to the Excel defaults, exactly what the Avalonia capture shows.
        new("dialog.OutlineGroup", () =>
            new OutlineSettingsDialog(OutlineSettingsPlanner.FromStored(null, null, null))),
        new("dialog.PageBreak", () =>
        {
            var wb = ParityDemoWorkbookFactory.Create();
            var sheet = wb.Sheets.Single();
            var a1 = new CellAddress(sheet.Id, 1, 1);
            return new PageBreakDialog(PageBreakDialogPlanner.BuildDefaultInput(new GridRange(a1, a1)));
        }),
        new("dialog.PasteNames", () =>
        {
            var wb = ParityDemoWorkbookFactory.Create();
            var sheet = wb.Sheets.Single();
            var commandPlan = DefinedNameUiPolicy.PlanUseInFormula(
                wb,
                range => PivotCreatePlanner.FormatRange(wb, sheet.Id, range),
                DefinedNameUiProfile.Wpf);
            return new PasteNamesDialog(PasteNamesPlanner.BuildItems(
                wb,
                range => PivotCreatePlanner.FormatRange(wb, sheet.Id, range)));
        }),
        new("dialog.PasteSpecial", () => new PasteSpecialDialog()),
        new("dialog.PictureCrop", () => new PictureCropDialog(CreateParityPicture())),
        new("dialog.PivotCalculatedField", () =>
        {
            var (_, _, pivot) = CreateParityPivot();
            return new PivotCalculatedFieldDialog(
                fieldNames: PivotControlPickerParityFixture.Headers,
                pivotTable: pivot);
        }),
        new("dialog.PivotCalculatedItem", () =>
        {
            var (wb, sheet, pivot) = CreateParityPivot();
            var itemNames = new Dictionary<int, IEnumerable<string>>();
            foreach (var layoutField in pivot.RowFields.Concat(pivot.ColumnFields))
                itemNames[layoutField.SourceFieldIndex] =
                    PivotSourceContext.ReadItems(wb, sheet, pivot, layoutField.SourceFieldIndex);
            return new PivotCalculatedItemDialog(
                PivotControlPickerParityFixture.Headers,
                0,
                itemNamesBySourceFieldIndex: itemNames,
                pivotTable: pivot);
        }),
        new("dialog.PivotChartOptions", () => new PivotChartOptionsDialog(CreateParityPivotChart())),
        new("dialog.PivotChartType", () => new PivotChartTypeDialog(ChartType.Column)),
        new("dialog.PivotFieldGrouping", () =>
        {
            var (_, _, pivot) = CreateParityPivot();
            return new PivotFieldGroupingDialog(PivotControlPickerParityFixture.Headers, pivot.RowFields[0]);
        }),
        new("dialog.PivotLabelFilter", () => new PivotLabelFilterDialog(0)),
        new("dialog.PivotSortOptions", () =>
        {
            var (_, _, pivot) = CreateParityPivot();
            return new PivotSortOptionsDialog(
                PivotControlPickerParityFixture.Headers[0],
                0,
                pivot.DataFields);
        }),
        new("dialog.PivotStyleGallery", () =>
        {
            var (_, _, pivot) = CreateParityPivot();
            return new PivotStyleGalleryDialog(pivot.StyleName);
        }),
        new("dialog.PivotTable", () =>
        {
            var wb = ParityDemoWorkbookFactory.Create();
            var sheet = wb.Sheets.Single();
            SeedParityPivotSource(sheet);
            var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 8, 5));
            return new PivotTableDialog(wb, sheet.Id, range);
        }),
        new("dialog.PivotTableDataSource", () =>
        {
            var (wb, sheet, pivot) = CreateParityPivot();
            return new PivotTableDataSourceDialog(
                PivotCreatePlanner.FormatRange(wb, sheet.Id, pivot.SourceRange),
                sheetId: sheet.Id,
                resolveSheetId: ResolveSheetId(wb));
        }),
        new("dialog.PivotTableName", () =>
        {
            var (_, _, pivot) = CreateParityPivot();
            return new PivotTableNameDialog(pivot.Name);
        }),
        new("dialog.PivotValueFilter", () =>
        {
            var (_, _, pivot) = CreateParityPivot();
            return new PivotValueFilterDialog(
                0,
                dataFieldNames: pivot.DataFields.Select(dataField => dataField.Name).ToList());
        }),
        new("dialog.Rotation", () => new RotationDialog(0, UiText.Get("MainWindowMessage_RotateObjectTitle"))),
        new("dialog.RowHeight", () => new RowHeightDialog(ParityRowHeight())),
        new("dialog.ScreenTip", () => new ScreenTipDialog("Quarterly sales")),
        new("dialog.SpellCheck", () => new SpellCheckDialog("Quaterly", "Quarterly")),
        new("dialog.TableResize", () =>
        {
            var wb = ParityDemoWorkbookFactory.Create();
            var sheet = wb.Sheets.Single();
            var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 4));
            var bus = new CommandBus(_ => new WorkbookCommandContext(wb));
            bus.Execute(wb.Id, new CreateStructuredTableCommand(sheet.Id, range, "TableStyleMedium2", firstRowHasHeaders: true));
            var table = sheet.StructuredTables.First();
            return new TextEntryDialog(
                UiText.Get("MainWindow_TooltipTitle_ResizeTable"),
                UiText.Get("TableDesign_TableRangeLabel"),
                TableResizePlanner.Capture(table));
        }),
        new("dialog.TextEntry", () =>
            new TextEntryDialog("Enter Text", "Text", "FreeX", automationId: "ParityTextEntryBox")),
        new("dialog.ThreadedComment", () =>
            new ThreadedCommentDialog("B2", new ThreadedComment("Please verify this total.", "Reviewer"))),
        new("dialog.TopBottomRule", () =>
            new TopBottomRuleDialog(ConditionalFormatDialogCatalog.Top10ItemsRule, ParityRuleRange())),
        new("dialog.UnhideWindow", () =>
        {
            // The dialog only lists display names; an inert window stand-in is enough to build the target.
            var hiddenWindow = System.Reflection.DispatchProxy.Create<IWorkbookWindow, InertWorkbookWindowProxy>();
            var targets = WorkbookWindowSelectionPlanner.BuildUnhideWindowTargets(
                [new WorkbookWindowSelectionEntry<IWorkbookWindow>(hiddenWindow, 1)],
                ParityDemoWorkbookFactory.WorkbookName,
                2);
            return new UnhideWindowDialog(targets);
        }),
        new("dialog.WorkbookTheme", () => new WorkbookThemeDialog(WorkbookTheme.Office, WorkbookThemeDialogMode.Theme)),
    ];

    /// <summary>Captures every supplemental surface (or only <paramref name="targetSurfaceId"/>).</summary>
    private static void CaptureSupplementalDialogs(
        string outDir,
        List<SurfaceResult> results,
        string? targetSurfaceId = null)
    {
        foreach (var surface in BuildSupplementalDialogs())
        {
            if (targetSurfaceId is not null &&
                !string.Equals(targetSurfaceId, surface.SurfaceId, StringComparison.Ordinal))
            {
                continue;
            }

            CaptureDialog(results, surface.SurfaceId, outDir, surface.Create, note: surface.Note);
        }
    }

    private static bool IsSupplementalDialogSurface(string surfaceId) =>
        BuildSupplementalDialogs().Any(surface => string.Equals(surface.SurfaceId, surfaceId, StringComparison.Ordinal));

    private static GridRange ParityRuleRange()
    {
        var sheetId = ParityDemoWorkbookFactory.Create().Sheets.Single().Id;
        return new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 5, 5));
    }

    private static double ParityRowHeight()
    {
        var sheet = ParityDemoWorkbookFactory.Create().Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        return RowColumnSizingPlanner.GetRowHeightDialogValue(sheet, new GridRange(a1, a1));
    }

    private static double ParityColumnWidth()
    {
        var sheet = ParityDemoWorkbookFactory.Create().Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        return RowColumnSizingPlanner.GetColumnWidthDialogValue(sheet, new GridRange(a1, a1));
    }

    /// <summary>The Avalonia <c>EnsureParityChart</c> chart: a column chart of A1:D4 titled "Quarterly Sales".</summary>
    private static ChartModel CreateParityChart(ChartType type)
    {
        var wb = ParityDemoWorkbookFactory.Create();
        var sheet = wb.Sheets.Single();
        var command = new AddChartCommand(
            sheet.Id,
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 4)),
            ChartType.Column,
            title: "Quarterly Sales",
            left: 260,
            top: 96,
            width: 360,
            height: 240);
        new CommandBus(_ => new WorkbookCommandContext(wb)).Execute(wb.Id, command);
        var chart = sheet.Charts.First(item => item.Id == command.ChartId);
        chart.Type = type;
        return chart;
    }

    private static ChartModel CreateParityPivotChart()
    {
        var (wb, sheet, pivot) = CreateParityPivot();
        var command = new AddPivotChartCommand(sheet.Id, pivot.Name, ChartType.Column);
        new CommandBus(_ => new WorkbookCommandContext(wb)).Execute(wb.Id, command);
        return sheet.Charts.First(chart => chart.IsPivotChart);
    }

    /// <summary>The Avalonia <c>EnsureParityPicture</c> picture.</summary>
    private static PictureModel CreateParityPicture()
    {
        var sheet = ParityDemoWorkbookFactory.Create().Sheets.Single();
        return new PictureModel
        {
            Name = "Parity picture",
            Anchor = new CellAddress(sheet.Id, 6, 5),
            Kind = PictureKind.Image,
            ImageBytes = [0x89, 0x50, 0x4E, 0x47],
            ContentType = "image/png",
            Width = 180,
            Height = 110,
            AltText = "Quarterly sales preview",
        };
    }

    private static CommentListWindow CreateParityCommentList()
    {
        var wb = ParityDemoWorkbookFactory.Create();
        var sheet = wb.Sheets.Single();
        var address = new CellAddress(sheet.Id, 2, 2);
        new CommandBus(_ => new WorkbookCommandContext(wb)).Execute(
            wb.Id,
            new SetThreadedCommentCommand(sheet.Id, address, "Review the quarterly total.", "FreeX"));
        return new CommentListWindow(
            UiText.Get("MainWindowMessage_CommentsTitle"),
            CommentListWindow.CreateThreadedCommentItems(sheet.ThreadedComments),
            _ => { },
            getPane: filter => ThreadedCommentCardPlanner.BuildPane(sheet, filter),
            getCard: cell => ThreadedCommentCardPlanner.BuildCard(sheet, cell),
            applyAction: (_, _) => { });
    }

    /// <summary>The Avalonia <c>EnsureParityPivot</c> pivot over the seeded A1:H8 source block.</summary>
    private static (Workbook Workbook, Sheet Sheet, PivotTableModel Pivot) CreateParityPivot()
    {
        var wb = ParityDemoWorkbookFactory.Create();
        var sheet = wb.Sheets.Single();
        SeedParityPivotSource(sheet);
        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 8, 5));
        var targetRange = new GridRange(new CellAddress(sheet.Id, 2, 7), new CellAddress(sheet.Id, 2, 7));
        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = sourceRange.ToString(),
        };
        for (var col = sourceRange.Start.Col; col <= sourceRange.End.Col; col++)
            cache.Fields.Add(new PivotCacheFieldModel(ParityPivotHeader(sheet, sourceRange.Start.Row, col)));
        wb.PivotCaches.Add(cache);

        var pivot = new PivotTableModel
        {
            Name = "ParityPivot",
            CacheId = cache.CacheId,
            SourceRange = sourceRange,
            TargetRange = targetRange,
            StyleName = PivotStyleGalleryPlanner.DefaultStyleName,
            ShowRowStripes = true,
            LastRenderedRange = new GridRange(
                targetRange.Start,
                new CellAddress(sheet.Id, targetRange.Start.Row + 4, targetRange.Start.Col + 2)),
        };
        pivot.RowFields.Add(new PivotFieldModel(0, SelectedItems: ["North", "South"]));
        pivot.DataFields.Add(new PivotDataFieldModel(4, "Sum of Revenue", "sum"));
        sheet.PivotTables.Add(pivot);
        return (wb, sheet, pivot);
    }

    private static string ParityPivotHeader(Sheet sheet, uint row, uint col) =>
        sheet.GetCell(row, col)?.Value switch
        {
            TextValue { Value.Length: > 0 } text => text.Value,
            NumberValue number => number.Value.ToString(CultureInfo.InvariantCulture),
            _ => $"Column{col}",
        };

    private static void SeedParityPivotSource(Sheet sheet)
    {
        string[][] rows =
        {
            ["Region", "Product", "Units", "Price", "Revenue"],
            ["North", "Widget", "120", "9.5", "1140"],
            ["South", "Gadget", "85", "14.25", "1211.25"],
            ["East", "Sprocket", "200", "3.75", "750"],
            ["West", "Gizmo", "64", "21", "1344"],
            ["North", "Cog", "310", "1.2", "372"],
            ["South", "Widget", "150", "9.5", "1425"],
            ["East", "Gadget", "95", "14.25", "1353.75"],
        };

        for (var r = 0; r < rows.Length; r++)
        {
            for (var c = 0; c < rows[r].Length; c++)
            {
                var text = rows[r][c];
                var value = r > 0 && c >= 2 &&
                    double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                        ? new NumberValue(number)
                        : (ScalarValue)new TextValue(text);
                sheet.SetCell(new CellAddress(sheet.Id, (uint)(r + 1), (uint)(c + 1)), Cell.FromValue(value));
            }
        }
    }
}
