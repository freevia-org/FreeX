using ClosedXML.Excel;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Integration.Tests;

/// <summary>
/// Regression tests for the R112 finding: CommandGuards.RejectIfSplitsArray/TryGetArrayExtent
/// treated a modern dynamic-array formula's anchor cell the same as a legacy Ctrl+Shift+Enter
/// (CSE) array's anchor, always requiring the whole array/spill extent to be included in an
/// edit/delete. Real Excel allows a modern dynamic array's anchor to be retyped/replaced directly
/// at any time (that's the defining UX difference from legacy CSE arrays, which still require the
/// whole originally-declared range to be selected/edited as a unit). See ArrayFormulaGuardTests
/// for the sibling non-anchor-member and whole-range-as-a-unit coverage this does not repeat.
/// </summary>
public class R112_ArrayAnchorEditGuardTests
{
    private const string CannotChangePartOfArrayMessage = "You cannot change part of an array.";

    private static MemoryStream BuildWorkbookWithMultiCellArrayFormula()
    {
        var ms = new MemoryStream();
        using (var xl = new XLWorkbook())
        {
            var ws = xl.AddWorksheet("S");
            ws.Cell(1, 1).Value = 1; ws.Cell(1, 2).Value = 2;   // A1:B1
            ws.Cell(2, 1).Value = 3; ws.Cell(2, 2).Value = 4;   // A2:B2
            ws.Cell(1, 5).Value = 10; ws.Cell(1, 6).Value = 20; // E1:F1
            ws.Cell(2, 5).Value = 30; ws.Cell(2, 6).Value = 40; // E2:F2
            // Multi-cell legacy array formula across H1:I2 (writes <f t="array" ref="H1:I2">).
            ws.Range("H1:I2").FormulaArrayA1 = "A1:B2+E1:F2";
            xl.SaveAs(ms);
        }
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public void EditCellsCommand_OnLegacyArrayAnchorAlone_BeforeRecalc_IsStillBlocked()
    {
        // No-regression sibling for the anchor-alone fix: a legacy CSE array's anchor is NOT the
        // modern-dynamic-array exception - Excel still requires the whole declared range to be
        // selected to redefine it, even before the first recalculation (provisional cached-spill
        // shape, Cell.LegacyArrayRows already populated straight from the loader).
        using var ms = BuildWorkbookWithMultiCellArrayFormula();
        var wb = new XlsxFileAdapter().Load(ms);
        var sheet = wb.GetSheetAt(0);
        var ctx = new TestCommandContext(wb);
        var anchor = new CellAddress(sheet.Id, 1, 8); // H1

        var outcome = EditCellsCommand.ForValue(sheet.Id, anchor, new NumberValue(999)).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Be(CannotChangePartOfArrayMessage);
    }

    [Fact]
    public void EditCellsCommand_OnLegacyArrayAnchorAlone_AfterRecalc_IsStillBlocked()
    {
        // Same no-regression check once the legacy CSE array has been promoted to a live
        // _spillAnchors entry by a recalculation - TryGetArrayExtent's "own anchor" branch must
        // still distinguish it from a modern dynamic array via Cell.LegacyArrayRows.
        using var ms = BuildWorkbookWithMultiCellArrayFormula();
        var wb = new XlsxFileAdapter().Load(ms);
        var sheet = wb.GetSheetAt(0);
        new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()).RecalculateAllFormulas(wb);
        var ctx = new TestCommandContext(wb);
        var anchor = new CellAddress(sheet.Id, 1, 8); // H1

        var outcome = EditCellsCommand.ForValue(sheet.Id, anchor, new NumberValue(999)).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Be(CannotChangePartOfArrayMessage);
        // The legacy array must have survived the rejected edit attempt intact.
        sheet.GetValue(1, 8).Should().Be(new NumberValue(11)); // H1
        sheet.GetValue(1, 9).Should().Be(new NumberValue(22)); // I1
        sheet.GetValue(2, 8).Should().Be(new NumberValue(33)); // H2
        sheet.GetValue(2, 9).Should().Be(new NumberValue(44)); // I2
    }

    [Fact]
    public void ClearContentsCommand_OnLegacyArrayAnchorAlone_IsStillBlocked()
    {
        // Same distinction on the ClearContentsCommand path (Delete key), which now shares the
        // same centralized CommandGuards.RejectIfSplitsArray anchor carve-out.
        using var ms = BuildWorkbookWithMultiCellArrayFormula();
        var wb = new XlsxFileAdapter().Load(ms);
        var sheet = wb.GetSheetAt(0);
        new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()).RecalculateAllFormulas(wb);
        var ctx = new TestCommandContext(wb);
        var anchor = new CellAddress(sheet.Id, 1, 8); // H1

        var outcome = new ClearContentsCommand(sheet.Id, new GridRange(anchor, anchor)).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Be(CannotChangePartOfArrayMessage);
        sheet.GetValue(1, 8).Should().Be(new NumberValue(11)); // H1 untouched
    }

    [Fact]
    public void ClearContentsCommand_OnDynamicArrayAnchorAlone_ClearsWholeSpill()
    {
        // Companion to EditCellsCommand_OnAnchorAlone_ForDynamicArray_IsAllowed on the
        // ClearContentsCommand (Delete key) path, now routed through the same shared choke point
        // in CommandGuards.RejectIfSplitsArray rather than ClearContentsCommand's own bespoke
        // pre-filter (which never checked Cell.LegacyArrayRows and would have wrongly allowed
        // this same anchor-alone shortcut for a legacy CSE array too).
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var anchor = new CellAddress(sheet.Id, 1, 1); // A1
        sheet.SetCell(anchor, Cell.FromFormula("SEQUENCE(3,1)"));
        var cells = new ScalarValue[3, 1]
        {
            { new NumberValue(1) },
            { new NumberValue(2) },
            { new NumberValue(3) },
        };
        sheet.SetSpillRange(anchor, new RangeValue(cells)); // spills A1:A3
        var ctx = new TestCommandContext(wb);

        var outcome = new ClearContentsCommand(sheet.Id, new GridRange(anchor, anchor)).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(anchor).Should().Be(BlankValue.Instance);
        sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(BlankValue.Instance);
        sheet.GetValue(new CellAddress(sheet.Id, 3, 1)).Should().Be(BlankValue.Instance);
    }
}
