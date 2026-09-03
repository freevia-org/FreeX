using FluentAssertions;
using FreeX.Core.Commands;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r264: Change Data Source. Re-confirming the dialog without editing the reference -- which is how a
/// user checks what the source currently IS -- writes back the same range and re-renders the same
/// cells.
///
/// <para>r231 held this command back because the obvious comparison could not fire: the snapshot's
/// <c>OriginalCache</c> is the LIVE cache object whenever Apply mutates in place, so comparing its
/// content against itself is always true. That is right, and it is only true of that one member --
/// every mutable field of the cache is captured beside it.</para>
/// </summary>
public sealed class R264_ChangePivotSourceNoOpTests
{
    private static CellAddress Addr(Sheet sheet, string a1) => CellAddress.Parse(a1, sheet.Id);

    private static GridRange Range(Sheet sheet, string start, string end) =>
        new(Addr(sheet, start), Addr(sheet, end));

    private static (Sheet Sheet, TestCommandContext Ctx, PivotTableModel Pivot) SetUpPivot()
    {
        var workbook = new Workbook("R264");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Category"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("A"));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("B"));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(20));
        sheet.SetCell(Addr(sheet, "A4"), new TextValue("C"));
        sheet.SetCell(Addr(sheet, "B4"), new NumberValue(30));

        workbook.PivotCaches.Add(new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = Range(sheet, "A1", "B3").ToString(),
        });
        workbook.PivotCaches[0].Fields.Add(new PivotCacheFieldModel("Category"));
        workbook.PivotCaches[0].Fields.Add(new PivotCacheFieldModel("Amount", ContainsNumber: true));

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

        var ctx = new TestCommandContext(workbook);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);
        // Settle: the first source-change populates the cache's shared items from the live data.
        new ChangePivotTableSourceCommand(sheet.Id, "PivotTable1", Range(sheet, "A1", "B3")).Apply(ctx);

        return (sheet, ctx, pivot);
    }

    [Fact]
    public void RepointingAtTheSameRangeIsANoOp()
    {
        var (sheet, ctx, _) = SetUpPivot();

        new ChangePivotTableSourceCommand(sheet.Id, "PivotTable1", Range(sheet, "A1", "B3")).Apply(ctx)
            .IsNoOp.Should().BeTrue("the pivot already reads exactly this range");
    }

    [Fact]
    public void RepointingAtALargerRangeIsNotANoOp()
    {
        var (sheet, ctx, pivot) = SetUpPivot();

        new ChangePivotTableSourceCommand(sheet.Id, "PivotTable1", Range(sheet, "A1", "B4")).Apply(ctx)
            .IsNoOp.Should().BeFalse("the third category enters the pivot");
        pivot.SourceRange.Should().Be(Range(sheet, "A1", "B4"));
    }

    /// <summary>
    /// A range change that renders identically: the extra row is blank, so no pivot cell moves --
    /// but the pivot's SourceRange and the cache's SourceReference both changed, and both round-trip
    /// into the saved file. Only the snapshot half of the decision can see this.
    /// </summary>
    [Fact]
    public void RepointingAtARangeWithNoNewDataIsStillNotANoOp()
    {
        var (sheet, ctx, pivot) = SetUpPivot();
        var renderedBefore = sheet.GetValue(4, 5);

        new ChangePivotTableSourceCommand(sheet.Id, "PivotTable1", Range(sheet, "A1", "C3")).Apply(ctx)
            .IsNoOp.Should().BeFalse("the stored source reference widened even though no row moved");

        sheet.GetValue(4, 5).Should().Be(renderedBefore, "the render really is unchanged");
        pivot.SourceRange.End.Should().Be(Addr(sheet, "C3"));
    }
}
