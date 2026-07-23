using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// Regression tests for three round-38 cut/move findings:
///
/// R38-commands-cut-move-2-1: a cross-sheet Cut+Paste (MoveRangeCommand with a destination on a
/// DIFFERENT sheet than the source) must follow real Excel's move semantics -- the moved formula
/// keeps pointing at exactly what it pointed at before (gaining an explicit source-sheet qualifier
/// only where the reference stays behind on the source sheet), and any OTHER formula elsewhere in
/// the workbook that referenced a moved cell must follow it to its new (sheet, row, col).
///
/// R38-commands-cut-move-2-2: cutting an entire structured table's range and pasting it elsewhere
/// must relocate the table's own Range (header/data/totals) along with the cells, so
/// Table[Column] structured references keep resolving against the moved data.
///
/// R38-commands-cut-move-2-3: a cross-sheet Cut of a merged cell must unmerge the vacated source
/// (ClearContentsCommand's isCutSource flag), not just clear its value -- but a plain, non-cut
/// "Clear Contents" on a merged cell must leave the merge in place, matching real Excel.
/// </summary>
public sealed class R38_CommandsCutMoveTests
{
    // ── R38-commands-cut-move-2-1: cross-sheet Cut+Paste formula reference rewrite ────────────

    [Fact]
    public void MoveRange_CrossSheetCutOfAbsoluteFormula_QualifiesSourceSheetAndPreservesValue()
    {
        var workbook = new Workbook("test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        var context = new TestCommandContext(workbook);

        var a1 = new CellAddress(sheet1.Id, 1, 1); // Sheet1!A1 = 100
        var b2 = new CellAddress(sheet1.Id, 2, 2); // Sheet1!B2 = "=$A$1"
        sheet1.SetCell(a1, new NumberValue(100));
        sheet1.SetFormula(b2, "$A$1");

        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var engine = new RecalcEngine(graph, evaluator);
        engine.RegisterFormulaDependencies(b2, FormulaEvaluator.ParseFormula("$A$1"), sheet1.Id, workbook);
        engine.Recalculate(workbook, [b2]);
        sheet1.GetValue(b2).Should().Be(new NumberValue(100));

        var destination = new CellAddress(sheet2.Id, 4, 4); // Sheet2!D4
        var command = new MoveRangeCommand(sheet1.Id, new GridRange(b2, b2), destination);

        var outcome = command.Apply(context);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        sheet1.GetCell(b2).Should().BeNull("the source cell is vacated by the move");
        var movedCell = sheet2.GetCell(destination)!;
        movedCell.FormulaText.Should().Be(
            "Sheet1!$A$1",
            "a cut/move never re-relativizes a formula -- it keeps pointing at the same source " +
            "cell, gaining an explicit sheet qualifier only because it now lives on a different sheet");

        engine.RegisterFormulaDependencies(
            destination, FormulaEvaluator.ParseFormula(movedCell.FormulaText!), sheet2.Id, workbook);
        engine.Recalculate(workbook, [destination]);
        sheet2.GetValue(destination).Should().Be(
            new NumberValue(100),
            "the moved formula must still read Sheet1!A1, not silently resolve to the blank Sheet2!A1");

        command.Revert(context);
        sheet1.GetCell(b2)!.FormulaText.Should().Be("$A$1");
        sheet2.GetCell(destination).Should().BeNull();
    }

    [Fact]
    public void MoveRange_CrossSheetCutOfRelativeFormula_KeepsPointingAtOriginalCellNotACopyOffset()
    {
        var workbook = new Workbook("test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        var context = new TestCommandContext(workbook);

        var a1 = new CellAddress(sheet1.Id, 1, 1); // Sheet1!A1 = 100
        var b2 = new CellAddress(sheet1.Id, 2, 2); // Sheet1!B2 = "=A1" (relative)
        sheet1.SetCell(a1, new NumberValue(100));
        sheet1.SetFormula(b2, "A1");

        var destination = new CellAddress(sheet2.Id, 4, 4); // Sheet2!D4
        var command = new MoveRangeCommand(sheet1.Id, new GridRange(b2, b2), destination);

        var outcome = command.Apply(context);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        sheet2.GetCell(destination)!.FormulaText.Should().Be(
            "Sheet1!A1",
            "a plain (relative) reference must also keep pointing at the original cell -- not get " +
            "shifted by the row/col delta between source and destination like a copy-paste would");
    }

    [Fact]
    public void MoveRange_CrossSheetCut_OtherFormulaElsewhereFollowsTheMovedCellAcrossSheets()
    {
        var workbook = new Workbook("test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        var context = new TestCommandContext(workbook);

        var b2 = new CellAddress(sheet1.Id, 2, 2); // Sheet1!B2 = 42 (moved)
        var c5 = new CellAddress(sheet1.Id, 5, 3); // Sheet1!C5 = "=B2" (references the moved cell)
        sheet1.SetCell(b2, new NumberValue(42));
        sheet1.SetFormula(c5, "B2");

        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var engine = new RecalcEngine(graph, evaluator);
        engine.RegisterFormulaDependencies(c5, FormulaEvaluator.ParseFormula("B2"), sheet1.Id, workbook);
        engine.Recalculate(workbook, [c5]);
        sheet1.GetValue(c5).Should().Be(new NumberValue(42));

        var destination = new CellAddress(sheet2.Id, 4, 4); // Sheet2!D4
        var command = new MoveRangeCommand(sheet1.Id, new GridRange(b2, b2), destination);

        var outcome = command.Apply(context);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        var c5Cell = sheet1.GetCell(c5)!;
        c5Cell.FormulaText.Should().Be(
            "Sheet2!D4",
            "a formula elsewhere in the workbook that referenced the cut cell must follow it to its " +
            "new sheet/address, not keep pointing at the now-blank source cell");
        outcome.AffectedCells.Should().Contain(c5, "the retargeted formula must be surfaced so the " +
            "standard recalculation pipeline picks it up");

        engine.RegisterFormulaDependencies(
            c5, FormulaEvaluator.ParseFormula(c5Cell.FormulaText!), sheet1.Id, workbook);
        engine.Recalculate(workbook, [c5]);
        sheet1.GetValue(c5).Should().Be(new NumberValue(42));

        command.Revert(context);
        sheet1.GetCell(c5)!.FormulaText.Should().Be("B2");
    }

    // ── R38-commands-cut-move-2-2: cutting an entire structured table relocates its Range ──────

    [Fact]
    public void MoveRange_CutEntireStructuredTable_RelocatesTableRangeAndKeepsStructuredRefWorking()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new TestCommandContext(workbook);

        // Table1: A1:B3 (header A1:B1, data A2:B3). Column1 data = 10, 20.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Column1"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Column2"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(20));
        var tableRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2));
        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = tableRange,
            HeaderRowCount = 1,
            Columns = { new StructuredTableColumnModel(1, "Column1"), new StructuredTableColumnModel(2, "Column2") }
        };
        sheet.StructuredTables.Add(table);

        var sumAddress = new CellAddress(sheet.Id, 1, 4); // D1 = SUM(Table1[Column1])
        sheet.SetFormula(sumAddress, "SUM(Table1[Column1])");

        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var engine = new RecalcEngine(graph, evaluator);
        engine.RegisterFormulaDependencies(
            sumAddress, FormulaEvaluator.ParseFormula("SUM(Table1[Column1])"), sheet.Id, workbook);
        engine.Recalculate(workbook, [sumAddress]);
        sheet.GetValue(sumAddress).Should().Be(new NumberValue(30));

        var destination = new CellAddress(sheet.Id, 1, 6); // F1
        var command = new MoveRangeCommand(sheet.Id, tableRange, destination);

        var outcome = command.Apply(context);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        var expectedNewRange = new GridRange(
            new CellAddress(sheet.Id, 1, 6),
            new CellAddress(sheet.Id, 3, 7)); // F1:G3
        var movedTable = sheet.StructuredTables.Should().ContainSingle().Which;
        movedTable.Name.Should().Be("Table1");
        movedTable.Range.Should().Be(expectedNewRange, "Table1's Range must follow the moved cells");
        movedTable.Columns.Should().HaveCount(2);

        // The SUM formula's own text never needed rewriting (structured references resolve by
        // table NAME, not address) -- but it must still resolve correctly now that Range moved.
        sheet.GetCell(sumAddress)!.FormulaText.Should().Be("SUM(Table1[Column1])");
        engine.RegisterFormulaDependencies(
            sumAddress, FormulaEvaluator.ParseFormula("SUM(Table1[Column1])"), sheet.Id, workbook);
        var report = engine.Recalculate(workbook, [sumAddress]);
        report.RecalculatedCells.Should().Contain(sumAddress);
        sheet.GetValue(sumAddress).Should().Be(
            new NumberValue(30),
            "SUM(Table1[Column1]) must keep computing over the relocated data, not silently drop to 0 " +
            "because Table1.Range still pointed at the now-blank source cells");

        command.Revert(context);
        var revertedTable = sheet.StructuredTables.Should().ContainSingle().Which;
        revertedTable.Range.Should().Be(tableRange);
    }

    // Sibling/no-regression: a move that only PARTIALLY overlaps a table must be rejected, exactly
    // like the existing merged-cell partial-overlap guard -- it must not silently split the table
    // or corrupt its Range.
    [Fact]
    public void MoveRange_PartiallyOverlappingStructuredTable_IsRejectedAndTableUnchanged()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new TestCommandContext(workbook);

        var tableRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)); // A1:B3
        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = tableRange,
            HeaderRowCount = 1,
            Columns = { new StructuredTableColumnModel(1, "Column1"), new StructuredTableColumnModel(2, "Column2") }
        };
        sheet.StructuredTables.Add(table);

        // Move only the table's first column (A1:A3) -- a partial overlap, not the whole table.
        var partialSource = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1));
        var destination = new CellAddress(sheet.Id, 1, 6); // F1
        var command = new MoveRangeCommand(sheet.Id, partialSource, destination);

        var outcome = command.Apply(context);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Be("Cannot move a range that intersects part of a table.");
        sheet.StructuredTables.Should().ContainSingle().Which.Range.Should().Be(tableRange);
    }

    // ── R38-commands-cut-move-2-3: cross-sheet Cut of a merged cell unmerges the source ────────

    [Fact]
    public void ClearContentsCommand_AsCutSource_UnmergesTheVacatedSourceMerge()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new TestCommandContext(workbook);

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var mergedRange = new GridRange(a1, b1); // A1:B1, merged
        sheet.AddMergedRegion(mergedRange);
        sheet.SetCell(a1, Cell.FromValue(new TextValue("Header")));

        var command = new ClearContentsCommand(sheet.Id, mergedRange, isCutSource: true);
        var outcome = command.Apply(context);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.MergedRegions.Should().BeEmpty(
            "a Cut must unmerge the vacated source -- real Excel turns A1:B1 back into two ordinary " +
            "blank cells once the merged content has moved elsewhere");
        sheet.GetCell(a1)!.Value.Should().Be(BlankValue.Instance);

        command.Revert(context);
        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(mergedRange);
        sheet.GetCell(a1)!.Value.Should().Be(new TextValue("Header"));
    }

    // Sibling/no-regression: a plain (non-cut) Clear Contents on a merged cell must leave the merge
    // in place -- only the value clears, matching real Excel's Delete-key/ribbon behavior.
    [Fact]
    public void ClearContentsCommand_PlainClear_LeavesMergeInPlace()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new TestCommandContext(workbook);

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var mergedRange = new GridRange(a1, b1); // A1:B1, merged
        sheet.AddMergedRegion(mergedRange);
        sheet.SetCell(a1, Cell.FromValue(new TextValue("Header")));

        var command = new ClearContentsCommand(sheet.Id, mergedRange);
        var outcome = command.Apply(context);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(
            mergedRange,
            "a plain Clear Contents (Delete key / ribbon Clear > Clear Contents) must NOT unmerge -- " +
            "only a Cut's tail-end clear does");
        sheet.GetCell(a1)!.Value.Should().Be(BlankValue.Instance);
    }

    // ── R76-commands-cut-move-4-1: cross-sheet Cut+Paste migrates named ranges and plain ─────────
    // ── chart/sparkline DataRange to the destination sheet, matching real Excel ──────────────────

    [Fact]
    public void MoveRange_CrossSheetCut_NamedRangeFullyContainedFollowsToDestinationSheet()
    {
        var workbook = new Workbook("test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        var context = new TestCommandContext(workbook);

        var sourceRange = new GridRange(
            new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 5, 1)); // Sheet1!A1:A5
        workbook.DefineNamedRange("Sales", sourceRange);

        var destination = new CellAddress(sheet2.Id, 1, 3); // Sheet2!C1
        var command = new MoveRangeCommand(sheet1.Id, sourceRange, destination);

        var outcome = command.Apply(context);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        var expectedRange = new GridRange(
            new CellAddress(sheet2.Id, 1, 3), new CellAddress(sheet2.Id, 5, 3)); // Sheet2!C1:C5
        workbook.NamedRanges["Sales"].Should().Be(
            expectedRange,
            "a name fully contained in the cut range must be re-anchored to the destination SHEET, " +
            "not just shifted by row/col delta while staying on the vacated source sheet");

        command.Revert(context);
        workbook.NamedRanges["Sales"].Should().Be(sourceRange, "undo must restore the name to its original sheet/range");
    }

    [Fact]
    public void MoveRange_CrossSheetCut_ChartAndSparklineDataRangeFullyContainedFollowToDestinationSheet()
    {
        var workbook = new Workbook("test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        var context = new TestCommandContext(workbook);

        var sourceRange = new GridRange(
            new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 4, 1)); // Sheet1!A1:A4

        // A chart hosted on sheet1 itself, plotting the range being cut.
        var chart = new ChartModel { Type = ChartType.Line, DataRange = sourceRange, Name = "Series" };
        sheet1.Charts.Add(chart);

        // A sparkline hosted OUTSIDE the cut range (at Sheet1!F1) whose data lives inside it -- the
        // sparkline's own Location must stay put; only its DataRange follows the moved data.
        var sparklineLocation = new CellAddress(sheet1.Id, 1, 6); // Sheet1!F1
        var sparkline = new SparklineModel
        {
            DataRange = sourceRange,
            Location = sparklineLocation,
            Kind = SparklineKind.Line,
            GroupId = 1
        };
        sheet1.Sparklines.Add(sparkline);

        var destination = new CellAddress(sheet2.Id, 1, 3); // Sheet2!C1
        var command = new MoveRangeCommand(sheet1.Id, sourceRange, destination);

        var outcome = command.Apply(context);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        var expectedRange = new GridRange(
            new CellAddress(sheet2.Id, 1, 3), new CellAddress(sheet2.Id, 4, 3)); // Sheet2!C1:C4
        chart.DataRange.Should().Be(
            expectedRange,
            "a plain chart DataRange fully contained in the cut range must follow the data to the " +
            "destination sheet, not keep pointing at the now-vacated source range");
        sparkline.Location.Should().Be(sparklineLocation, "the sparkline itself did not move");
        sparkline.DataRange.Should().Be(
            expectedRange,
            "the sparkline's DataRange must also follow the moved data across sheets");

        command.Revert(context);
        chart.DataRange.Should().Be(sourceRange, "undo must restore the chart's DataRange to its original sheet/range");
        sparkline.DataRange.Should().Be(sourceRange, "undo must restore the sparkline's DataRange to its original sheet/range");
    }

    // Sibling/no-regression: the pre-existing SAME-SHEET named-range migration (already working
    // before this fix) must keep working after threading the destination sheet through
    // TranslateFullyContainedNamedRanges -- a same-sheet move's destination is on the same sheet as
    // the source, so it must behave identically to a plain in-place row/col shift.
    [Fact]
    public void MoveRange_SameSheetMove_NamedRangeFullyContainedStillFollowsWithinSheet()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new TestCommandContext(workbook);

        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)); // A1:A5
        workbook.DefineNamedRange("Sales", sourceRange);

        var destination = new CellAddress(sheet.Id, 1, 3); // C1
        var command = new MoveRangeCommand(sheet.Id, sourceRange, destination);

        var outcome = command.Apply(context);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        var expectedRange = new GridRange(
            new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 5, 3)); // C1:C5
        workbook.NamedRanges["Sales"].Should().Be(expectedRange);

        command.Revert(context);
        workbook.NamedRanges["Sales"].Should().Be(sourceRange);
    }
}
