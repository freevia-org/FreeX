using FluentAssertions;
using FreeX.Core.Commands;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r259: the two Options dialogs. Both pre-fill every control from current state, so pressing OK
/// without changing anything is the ordinary case, and both wrote every setting back unconditionally.
///
/// <para>r219 singled out the PivotTable one: "its Apply is a 25-field assignment block, and
/// hand-listing that many fields in a guard is precisely the brittle mirror r218 avoided". Nothing is
/// hand-listed in the fix -- the decision re-runs the snapshot's own Capture and compares -- so these
/// tests check a handful of settings from opposite ends of that block to show the coverage is real
/// rather than concentrated on whichever field a guard happened to name.</para>
/// </summary>
public sealed class R259_PivotOptionsNoOpTests
{
    private static CellAddress Addr(Sheet sheet, string a1) => CellAddress.Parse(a1, sheet.Id);

    private static GridRange Range(Sheet sheet, string start, string end) =>
        new(Addr(sheet, start), Addr(sheet, end));

    private static (Sheet Sheet, TestCommandContext Ctx, PivotTableModel Pivot) SetUpPivot()
    {
        var workbook = new Workbook("PivotOptionsNoOpTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Category"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("A"));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("B"));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(20));

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "D3", "F8"),
            ReportLayout = PivotReportLayout.Tabular,
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        // Off by default in this fixture: see AnApplyThatAutofitsColumnsIsNotANoOp -- with it on, the
        // first apply legitimately resizes columns, which is a real change and not a no-op.
        pivot.AutofitColumnsOnUpdate = false;
        var ctx = new TestCommandContext(workbook);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        return (sheet, ctx, pivot);
    }

    /// <summary>The dialog's own defaults: every argument read straight off the pivot.</summary>
    private static ConfigurePivotTableOptionsCommand ReapplyCurrent(
        Sheet sheet,
        PivotTableModel pivot,
        bool? showRowGrandTotals = null,
        string? emptyValueText = null,
        bool? mergeAndCenterLabels = null) =>
        new(
            sheet.Id,
            "PivotTable1",
            showRowGrandTotals ?? pivot.ShowRowGrandTotals,
            pivot.ShowColumnGrandTotals,
            pivot.ShowSubtotals,
            pivot.SubtotalPlacement,
            pivot.RepeatItemLabels,
            pivot.BlankLineAfterItems,
            pivot.StyleName,
            showRowHeaders: pivot.ShowRowHeaders,
            showColumnHeaders: pivot.ShowColumnHeaders,
            showRowStripes: pivot.ShowRowStripes,
            showColumnStripes: pivot.ShowColumnStripes,
            reportLayout: pivot.ReportLayout,
            emptyValueText: emptyValueText ?? pivot.EmptyValueText,
            updateEmptyValueText: true,
            compactRowLabelIndent: pivot.CompactRowLabelIndent,
            showFieldHeaders: pivot.ShowFieldHeaders,
            showContextualTooltips: pivot.ShowContextualTooltips,
            showPropertiesInTooltips: pivot.ShowPropertiesInTooltips,
            showClassicLayout: pivot.ShowClassicLayout,
            mergeAndCenterLabels: mergeAndCenterLabels ?? pivot.MergeAndCenterLabels,
            showItemsWithNoDataOnRows: pivot.ShowItemsWithNoDataOnRows,
            showItemsWithNoDataOnColumns: pivot.ShowItemsWithNoDataOnColumns,
            pageOverThenDown: pivot.PageOverThenDown,
            pageWrap: pivot.PageWrap);

    [Fact]
    public void ConfigurePivotTableOptionsCommand_ReapplyingTheCurrentOptionsIsANoOp()
    {
        var (sheet, ctx, pivot) = SetUpPivot();

        ReapplyCurrent(sheet, pivot).Apply(ctx)
            .IsNoOp.Should().BeTrue("every setting is handed back exactly as the pivot holds it");
    }

    /// <summary>
    /// The autofit half of the decision, which is not redundant with the other two: with
    /// <c>AutofitColumnsOnUpdate</c> on (Excel's default), applying the options resizes the rendered
    /// columns, so the FIRST apply after a plain refresh changes real state even though every option
    /// is handed back unchanged and every rendered cell keeps its value. Only the column-width
    /// comparison can see that.
    ///
    /// <para>This is also why the fixture above turns autofit off: an apply that legitimately
    /// resizes columns is not a no-op, and a test asserting otherwise would be asserting a bug.</para>
    /// </summary>
    [Fact]
    public void ConfigurePivotTableOptionsCommand_AnApplyThatAutofitsColumnsIsNotANoOp()
    {
        var (sheet, ctx, pivot) = SetUpPivot();
        pivot.AutofitColumnsOnUpdate = true;

        ReapplyCurrent(sheet, pivot).Apply(ctx)
            .IsNoOp.Should().BeFalse("the autofit resized the pivot's rendered columns");

        ReapplyCurrent(sheet, pivot).Apply(ctx)
            .IsNoOp.Should().BeTrue("the widths are already what the autofit produces");
    }

    [Fact]
    public void ConfigurePivotTableOptionsCommand_TogglingAGrandTotalIsNotANoOp()
    {
        var (sheet, ctx, pivot) = SetUpPivot();

        ReapplyCurrent(sheet, pivot, showRowGrandTotals: !pivot.ShowRowGrandTotals).Apply(ctx)
            .IsNoOp.Should().BeFalse("a grand-total row is added or removed from the render");
    }

    /// <summary>
    /// A setting from the far end of the block, and one that changes no rendered cell in this
    /// fixture -- so it is caught by the snapshot comparison rather than by the cell comparison.
    /// </summary>
    [Fact]
    public void ConfigurePivotTableOptionsCommand_ChangingTheEmptyValueTextIsNotANoOp()
    {
        var (sheet, ctx, pivot) = SetUpPivot();

        ReapplyCurrent(sheet, pivot, emptyValueText: "n/a").Apply(ctx)
            .IsNoOp.Should().BeFalse("EmptyValueText round-trips into the saved file");
    }

    [Fact]
    public void ConfigurePivotTableOptionsCommand_ChangingMergeAndCenterLabelsIsNotANoOp()
    {
        var (sheet, ctx, pivot) = SetUpPivot();

        ReapplyCurrent(sheet, pivot, mergeAndCenterLabels: !pivot.MergeAndCenterLabels).Apply(ctx)
            .IsNoOp.Should().BeFalse();
    }
}
