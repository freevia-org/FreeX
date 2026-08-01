using ClosedXML.Excel;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Integration.Tests;

/// <summary>
/// Regression tests for the K12 finding: editing or deleting a single cell that belongs to a
/// legacy CSE array (t="array" over a multi-cell range) or a live dynamic-array spill range must
/// be blocked with Excel's "You cannot change part of an array" error, not silently allowed
/// (which corrupts the array on the next recalculation). Covers EditCellsCommand (single-cell
/// edit/anchor edit), ClearContentsCommand (Delete key), and the "edit the whole array as a unit"
/// allowance for both live dynamic-array spills and provisional (not-yet-recalculated) legacy CSE
/// arrays loaded straight from an XLSX.
/// </summary>
public class ArrayFormulaGuardTests
{
    private const string CannotChangePartOfArrayMessage = "You cannot change part of an array.";

    // ---------------------------------------------------------------------------
    // Live dynamic-array spill (in-session, e.g. =A1:B2 spilling H1:I2)
    // ---------------------------------------------------------------------------

    private static (Workbook Workbook, Sheet Sheet, CellAddress Anchor, ICommandContext Ctx) MakeLiveSpillSetup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var anchor = new CellAddress(sheet.Id, 1, 8); // H1
        sheet.SetCell(anchor, Cell.FromFormula("A1:B2"));
        var cells = new ScalarValue[2, 2]
        {
            { new NumberValue(11), new NumberValue(22) },
            { new NumberValue(33), new NumberValue(44) }
        };
        sheet.SetSpillRange(anchor, new RangeValue(cells)); // spills to H1:I2
        return (wb, sheet, anchor, new TestCommandContext(wb));
    }

    [Fact]
    public void EditCellsCommand_OnNonAnchorSpillMember_IsBlocked()
    {
        var (_, sheet, _, ctx) = MakeLiveSpillSetup();
        var member = new CellAddress(sheet.Id, 1, 9); // I1 - covered, non-anchor

        var outcome = EditCellsCommand.ForValue(sheet.Id, member, new NumberValue(999)).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Be(CannotChangePartOfArrayMessage);
        // The member must be untouched - no silent corruption.
        sheet.GetValue(member).Should().Be(new NumberValue(22));
    }

    [Fact]
    public void EditCellsCommand_OnAnchorAlone_ForDynamicArray_IsAllowed()
    {
        // R112-array-anchor-edit: real Excel always allows retyping/replacing a modern dynamic
        // array's anchor cell directly - that's the defining UX difference from a legacy
        // Ctrl+Shift+Enter (CSE) array, whose anchor still requires the whole declared range to be
        // selected. Editing the anchor alone naturally clears/re-establishes the spill.
        var (_, sheet, anchor, ctx) = MakeLiveSpillSetup();
        var member = new CellAddress(sheet.Id, 1, 9); // I1 - was part of the old spill

        var outcome = EditCellsCommand.ForValue(sheet.Id, anchor, new NumberValue(999)).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(anchor).Should().Be(new NumberValue(999));
        // The old spill's members must have been vacated by the anchor's own SetCell, not left
        // dangling with stale values as if the array were still live.
        sheet.GetValue(member).Should().Be(BlankValue.Instance);
    }

    [Fact]
    public void ClearContentsCommand_OnSingleSpillMember_IsBlocked()
    {
        var (_, sheet, _, ctx) = MakeLiveSpillSetup();
        var member = new CellAddress(sheet.Id, 2, 9); // I2 - covered, non-anchor

        var outcome = new ClearContentsCommand(sheet.Id, new GridRange(member, member)).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Be(CannotChangePartOfArrayMessage);
        sheet.GetValue(member).Should().Be(new NumberValue(44));
    }

    [Fact]
    public void EditCellsCommand_OnWholeSpillRangeAsUnit_IsAllowed()
    {
        var (_, sheet, anchor, ctx) = MakeLiveSpillSetup();
        var edits = new (CellAddress Address, Cell NewCell)[]
        {
            (anchor, Cell.FromValue(new NumberValue(1))),
            (new CellAddress(sheet.Id, 1, 9), Cell.FromValue(new NumberValue(2))),
            (new CellAddress(sheet.Id, 2, 8), Cell.FromValue(new NumberValue(3))),
            (new CellAddress(sheet.Id, 2, 9), Cell.FromValue(new NumberValue(4))),
        };

        var outcome = new EditCellsCommand(sheet.Id, edits).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
    }

    [Fact]
    public void ClearContentsCommand_OnWholeSpillRangeAsUnit_IsAllowed()
    {
        var (_, sheet, anchor, ctx) = MakeLiveSpillSetup();
        var wholeRange = new GridRange(anchor, new CellAddress(sheet.Id, 2, 9)); // H1:I2

        var outcome = new ClearContentsCommand(sheet.Id, wholeRange).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
    }

    [Fact]
    public void EditCellsCommand_OnUnrelatedCell_StillWorks()
    {
        // Sanity check: the guard must not false-positive on ordinary cells when the sheet
        // does contain an unrelated live spill elsewhere.
        var (_, sheet, _, ctx) = MakeLiveSpillSetup();
        var unrelated = new CellAddress(sheet.Id, 20, 20);

        var outcome = EditCellsCommand.ForValue(sheet.Id, unrelated, new TextValue("ok")).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(unrelated).Should().Be(new TextValue("ok"));
    }

    // ---------------------------------------------------------------------------
    // Legacy CSE array loaded from XLSX, not yet recalculated (provisional spill cells) -
    // mirrors the fixture in LegacyArrayFormulaTests.cs.
    // ---------------------------------------------------------------------------

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
    public void EditCellsCommand_OnProvisionalLegacyArrayMember_BeforeRecalc_IsBlocked()
    {
        using var ms = BuildWorkbookWithMultiCellArrayFormula();
        var wb = new XlsxFileAdapter().Load(ms);
        var sheet = wb.GetSheetAt(0);
        var ctx = new TestCommandContext(wb);

        // Before any recalculation, I1 (row 1, col 9) exists only as a provisional cached-spill
        // cell tagged to the H1 anchor (row 1, col 8) - not yet promoted to a live _spillValues entry.
        var member = new CellAddress(sheet.Id, 1, 9);

        var outcome = EditCellsCommand.ForValue(sheet.Id, member, new NumberValue(999)).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Be(CannotChangePartOfArrayMessage);
    }

    [Fact]
    public void EditCellsCommand_OnProvisionalLegacyArrayMember_AfterRecalc_IsBlocked()
    {
        using var ms = BuildWorkbookWithMultiCellArrayFormula();
        var wb = new XlsxFileAdapter().Load(ms);
        var sheet = wb.GetSheetAt(0);
        new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()).RecalculateAllFormulas(wb);
        var ctx = new TestCommandContext(wb);

        // After recalc, I1 is a live spill target (owned by the H1 anchor's _spillAnchors entry)
        // rather than a provisional cell - the guard must catch this shape too.
        var member = new CellAddress(sheet.Id, 1, 9);

        var outcome = EditCellsCommand.ForValue(sheet.Id, member, new NumberValue(999)).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Be(CannotChangePartOfArrayMessage);
        // Confirm the array survived the rejected edit attempt - no #SPILL! collapse.
        sheet.GetValue(1, 8).Should().Be(new NumberValue(11)); // H1
        sheet.GetValue(2, 8).Should().Be(new NumberValue(33)); // H2
        sheet.GetValue(2, 9).Should().Be(new NumberValue(44)); // I2
    }

    [Fact]
    public void ClearContentsCommand_OnProvisionalLegacyArrayMember_BeforeRecalc_IsBlocked()
    {
        using var ms = BuildWorkbookWithMultiCellArrayFormula();
        var wb = new XlsxFileAdapter().Load(ms);
        var sheet = wb.GetSheetAt(0);
        var ctx = new TestCommandContext(wb);
        var member = new CellAddress(sheet.Id, 2, 8); // H2

        var outcome = new ClearContentsCommand(sheet.Id, new GridRange(member, member)).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Be(CannotChangePartOfArrayMessage);
    }

    [Fact]
    public void EditCellsCommand_OnWholeLegacyArrayRangeAsUnit_BeforeRecalc_IsAllowed()
    {
        using var ms = BuildWorkbookWithMultiCellArrayFormula();
        var wb = new XlsxFileAdapter().Load(ms);
        var sheet = wb.GetSheetAt(0);
        var ctx = new TestCommandContext(wb);

        var edits = new (CellAddress Address, Cell NewCell)[]
        {
            (new CellAddress(sheet.Id, 1, 8), Cell.FromValue(new NumberValue(1))), // H1 (anchor)
            (new CellAddress(sheet.Id, 1, 9), Cell.FromValue(new NumberValue(2))), // I1
            (new CellAddress(sheet.Id, 2, 8), Cell.FromValue(new NumberValue(3))), // H2
            (new CellAddress(sheet.Id, 2, 9), Cell.FromValue(new NumberValue(4))), // I2
        };

        var outcome = new EditCellsCommand(sheet.Id, edits).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
    }
}
