using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class PivotTableCommandTests
{
    private static void SeedData(Sheet sheet)
    {
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Category"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("A"));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("B"));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(20));
    }

    private static (Sheet Sheet, TestCommandContext Context, PivotTableModel Pivot) CreateBasicPivotReport(string workbookName)
    {
        var workbook = new Workbook(workbookName);
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var ctx = new TestCommandContext(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "D3", "F6"),
            // R90-render-pivot-layout-5-3: pin the (former) Tabular default -- this shared helper's many
            // callers (move/rename/refresh/etc. command tests) assert the row field's own name as the
            // header text, not the new Compact "Row Labels" default; none of them are testing the
            // pivot-creation default itself.
            ReportLayout = PivotReportLayout.Tabular
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);
        return (sheet, ctx, pivot);
    }

    private static PivotTableModel CreateCategoryAmountPivot(Sheet sheet)
    {
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "D3", "F7")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        return pivot;
    }

    private static PivotTableModel CreateDateAmountPivot(Sheet sheet)
    {
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "D3", "F8")
        };
        pivot.RowFields.Add(new PivotFieldModel(0, Grouping: PivotFieldGrouping.Day));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        return pivot;
    }

    private static ConfigurePivotTableOptionsCommand CreateBasicPivotOptionsCommand(
        SheetId sheetId,
        string pivotTableName,
        bool showRowGrandTotals = true) =>
        new(
            sheetId,
            pivotTableName,
            showRowGrandTotals,
            showColumnGrandTotals: true,
            showSubtotals: true,
            PivotSubtotalPlacement.Bottom,
            repeatItemLabels: false,
            blankLineAfterItems: false,
            styleName: "PivotStyleMedium9");

    private static void SeedTimelineData(Sheet sheet)
    {
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Date"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), DateTimeValue.FromDateTime(new DateTime(2026, 1, 5)));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), DateTimeValue.FromDateTime(new DateTime(2026, 2, 2)));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(30));
    }

    private static CellAddress Addr(Sheet sheet, string a1) => CellAddress.Parse(a1, sheet.Id);

    private static GridRange Range(Sheet sheet, string start, string end) =>
        new(Addr(sheet, start), Addr(sheet, end));

}
