using FluentAssertions;
using FreeX.Core.Commands;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r263: the slicer and timeline commands. Clicking a slicer tile back to the selection already in
/// effect, or dragging a timeline handle back where it was, re-renders every bound pivot and writes
/// the same state -- and both are the most ordinary way a user reaches these commands twice.
/// </summary>
public sealed class R263_SlicerTimelineNoOpTests
{
    private static CellAddress Addr(Sheet sheet, string a1) => CellAddress.Parse(a1, sheet.Id);

    private static GridRange Range(Sheet sheet, string start, string end) =>
        new(Addr(sheet, start), Addr(sheet, end));

    private static (Workbook Wb, Sheet Sheet, TestCommandContext Ctx) SetUpPivotWithSlicer()
    {
        var workbook = new Workbook("R263");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Category"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("A"));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("B"));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(20));
        sheet.SetCell(Addr(sheet, "A4"), new TextValue("C"));
        sheet.SetCell(Addr(sheet, "B4"), new NumberValue(30));

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B4"),
            TargetRange = Range(sheet, "D3", "F9"),
            ReportLayout = PivotReportLayout.Tabular,
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        workbook.Slicers.Add(new SlicerModel
        {
            Name = "Category Slicer",
            CacheName = "Slicer_Category",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Category",
            SelectedItems = { "A", "B" },
            SelectionCaptured = true,
        });

        var ctx = new TestCommandContext(workbook);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        return (workbook, sheet, ctx);
    }

    [Fact]
    public void SetSlicerSelection_ReapplyingTheCurrentSelectionIsANoOp()
    {
        var (_, _, ctx) = SetUpPivotWithSlicer();

        // Settle first: the initial selection has not yet been pushed through the command, so the
        // first apply legitimately filters the pivot.
        new SetSlicerSelectionCommand("Category Slicer", ["A", "B"]).Apply(ctx);

        new SetSlicerSelectionCommand("Category Slicer", ["A", "B"]).Apply(ctx)
            .IsNoOp.Should().BeTrue("the same tiles are already selected and the pivots re-render the same");
    }

    [Fact]
    public void SetSlicerSelection_ChangingTheSelectionIsNotANoOp()
    {
        var (_, _, ctx) = SetUpPivotWithSlicer();
        new SetSlicerSelectionCommand("Category Slicer", ["A", "B"]).Apply(ctx);

        new SetSlicerSelectionCommand("Category Slicer", ["A"]).Apply(ctx)
            .IsNoOp.Should().BeFalse("deselecting B removes its row from the pivot");
    }

    [Fact]
    public void SetSlicerSelection_SelectingAnAdditionalItemIsNotANoOp()
    {
        var (_, _, ctx) = SetUpPivotWithSlicer();
        new SetSlicerSelectionCommand("Category Slicer", ["A", "B"]).Apply(ctx);

        new SetSlicerSelectionCommand("Category Slicer", ["A", "B", "C"]).Apply(ctx)
            .IsNoOp.Should().BeFalse("C's row appears in the pivot");
    }

    private static (Sheet Sheet, TestCommandContext Ctx) SetUpPivotWithTimeline()
    {
        var workbook = new Workbook("R263Timeline");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Date"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("2024-01-15"));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("2024-06-15"));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(20));

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "D3", "F9"),
            ReportLayout = PivotReportLayout.Tabular,
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        workbook.Timelines.Add(new TimelineModel
        {
            Name = "Date Timeline",
            CacheName = "Timeline_Date",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Date",
        });

        var ctx = new TestCommandContext(workbook);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        return (sheet, ctx);
    }

    [Fact]
    public void SetTimelineRange_ReapplyingTheCurrentRangeIsANoOp()
    {
        var (_, ctx) = SetUpPivotWithTimeline();
        new SetTimelineRangeCommand("Date Timeline", "2024-01-01", "2024-12-31").Apply(ctx);

        new SetTimelineRangeCommand("Date Timeline", "2024-01-01", "2024-12-31").Apply(ctx)
            .IsNoOp.Should().BeTrue("the same dates are already selected");
    }

    [Fact]
    public void SetTimelineRange_ChangingTheRangeIsNotANoOp()
    {
        var (_, ctx) = SetUpPivotWithTimeline();
        new SetTimelineRangeCommand("Date Timeline", "2024-01-01", "2024-12-31").Apply(ctx);

        new SetTimelineRangeCommand("Date Timeline", "2024-01-01", "2024-03-31").Apply(ctx)
            .IsNoOp.Should().BeFalse("narrowing the range filters out the June row");
    }

    /// <summary>
    /// The slicer-selection clause in isolation. Selecting an item that does not occur in the source
    /// data leaves every rendered pivot cell identical -- the render comparison sees nothing -- but
    /// the slicer's own SelectedItems list changed, and that list round-trips into the saved file.
    /// Without the selection comparison this reports a no-op and the selection is lost from undo.
    /// </summary>
    [Fact]
    public void SetSlicerSelection_SelectingAnItemAbsentFromTheDataIsStillNotANoOp()
    {
        var (wb, sheet, ctx) = SetUpPivotWithSlicer();
        new SetSlicerSelectionCommand("Category Slicer", ["A", "B"]).Apply(ctx);
        var renderedBefore = sheet.GetValue(4, 5);

        new SetSlicerSelectionCommand("Category Slicer", ["A", "B", "Zed"]).Apply(ctx)
            .IsNoOp.Should().BeFalse("the stored selection gained an item even though no row moved");

        sheet.GetValue(4, 5).Should().Be(renderedBefore, "the render really is unchanged");
        wb.Slicers[0].SelectedItems.Should().Contain("Zed");
    }
}
