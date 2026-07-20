using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Round-52 fresh-lens review fixes for the "commands" bucket:
///   R52-commands-clear-delete-3-1: band-scoped Insert/Delete Cells never shifted or cleared
///   style-only (formatted-but-empty) cells, silently destroying or misplacing formatting.
///   R52-commands-data-validation-apply-3-1: pasting DV onto one cell of a larger existing rule
///   deleted the whole rule instead of shrinking it to the surviving portion.
///   R52-commands-data-validation-apply-3-2: paste's pre-clear step ignored AdditionalRanges.
///   R52-commands-data-validation-apply-3-3: applying a new rule only replaced a rule matched by
///   exact Id/AppliesTo, leaving a differently-anchored overlapping rule active alongside it.
/// </summary>
public class R52_CommandsClearDeleteAndDataValidationTests
{
    private static (Workbook workbook, Sheet sheet) MakeWorkbook() =>
        TestWorkbookFixture.CreateWorkbook();

    private static GridRange RangeAt(Sheet sheet, uint row, uint col)
    {
        var addr = new CellAddress(sheet.Id, row, col);
        return new GridRange(addr, addr);
    }

    private static GridRange RangeAt(Sheet sheet, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(new CellAddress(sheet.Id, startRow, startCol), new CellAddress(sheet.Id, endRow, endCol));

    // ─── R52-commands-clear-delete-3-1: style-only cells must shift with the band ────────────

    [Fact]
    public void DeleteCellsCommand_ShiftLeft_MovesStyleOnlyCellIntoTheGap()
    {
        // Row 5: B5 empty/unstyled, C5 empty but style-only (a fill color on an empty cell),
        // D5 = "Total". Delete B5 with Shift Left: Excel moves the whole row band left by one
        // column as a unit (value + format together), so C5's style-only fill should land at B5
        // and D5's "Total" should land at C5.
        var (wb, sheet) = MakeWorkbook();
        var styleId = new StyleId(42);
        sheet.SetStyleOnly(5, 3, styleId); // C5
        sheet.SetCell(new CellAddress(sheet.Id, 5, 4), new Cell { Value = new TextValue("Total") }); // D5

        var bus = new CommandBus(_ => new TestCommandContext(wb));
        var deleteRange = RangeAt(sheet, 5, 2); // B5
        bus.Execute(wb.Id, new DeleteCellsCommand(sheet.Id, deleteRange, DeleteCellsShiftDirection.Left));

        sheet.GetStyleOnly(5, 2).Should().Be(styleId,
            "C5's style-only fill must shift left into B5 along with the rest of the band");
        sheet.GetStyleOnly(5, 3).Should().BeNull(
            "the style-only entry must be cleared from its old address, not duplicated");
        sheet.GetCell(new CellAddress(sheet.Id, 5, 3))?.Value.Should().Be(new TextValue("Total"),
            "D5's value should shift left into C5");
    }

    [Fact]
    public void DeleteCellsCommand_ShiftLeft_StillMovesValueCellsAndLeavesVacatedColumnBlank()
    {
        // Sibling no-regression check: plain value-cell shifting (the pre-existing behavior this
        // fix must not disturb) still works, and the rightmost vacated column ends up truly blank.
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 5, 3), new Cell { Value = new TextValue("C") }); // C5
        sheet.SetCell(new CellAddress(sheet.Id, 5, 4), new Cell { Value = new TextValue("D") }); // D5

        var bus = new CommandBus(_ => new TestCommandContext(wb));
        var deleteRange = RangeAt(sheet, 5, 2); // B5
        bus.Execute(wb.Id, new DeleteCellsCommand(sheet.Id, deleteRange, DeleteCellsShiftDirection.Left));

        sheet.GetCell(new CellAddress(sheet.Id, 5, 2))?.Value.Should().Be(new TextValue("C"));
        sheet.GetCell(new CellAddress(sheet.Id, 5, 3))?.Value.Should().Be(new TextValue("D"));
        sheet.GetCell(new CellAddress(sheet.Id, 5, 4)).Should().BeNull("the vacated trailing column must end up blank");
    }

    [Fact]
    public void DeleteCellsCommand_ShiftLeft_Undo_RestoresStyleOnlyToOriginalAddress()
    {
        var (wb, sheet) = MakeWorkbook();
        var styleId = new StyleId(7);
        sheet.SetStyleOnly(5, 3, styleId); // C5

        var bus = new CommandBus(_ => new TestCommandContext(wb));
        var deleteRange = RangeAt(sheet, 5, 2); // B5
        bus.Execute(wb.Id, new DeleteCellsCommand(sheet.Id, deleteRange, DeleteCellsShiftDirection.Left));
        bus.Undo(wb.Id);

        sheet.GetStyleOnly(5, 3).Should().Be(styleId, "undo should restore the style-only entry to its original address");
        sheet.GetStyleOnly(5, 2).Should().BeNull("undo must not leave a duplicate at the shifted address");
    }

    // ─── R52-commands-data-validation-apply-3-1/-3-2: paste must not wipe out untouched cells ──

    [Fact]
    public void PasteDataValidationCommand_Apply_ShrinksOverlappingRuleInsteadOfDeletingIt()
    {
        var (wb, sheet) = MakeWorkbook();
        var largeRuleRange = RangeAt(sheet, 1, 1, 10, 1); // A1:A10
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = largeRuleRange,
            Type = DvType.List,
            Formula1 = "Yes,No",
        });
        // Source being copied: B1 with a WholeNumber rule.
        var sourceRange = RangeAt(sheet, 1, 2); // B1
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = sourceRange,
            Type = DvType.WholeNumber,
            Operator = DvOperator.GreaterThanOrEqual,
            Formula1 = "1",
        });

        var bus = new CommandBus(_ => new TestCommandContext(wb));
        var destination = new CellAddress(sheet.Id, 5, 1); // A5, inside A1:A10
        bus.Execute(wb.Id, new PasteDataValidationCommand(sheet.Id, sourceRange, destination, transpose: false));

        // The original List rule survives as two remainder fragments (A1:A4, A6:A10), the source
        // rule at B1 is untouched (paste never removes the copied-FROM rule), and A5 gets the
        // newly pasted WholeNumber rule -- 4 rules total.
        sheet.DataValidations.Should().HaveCount(4);
        sheet.DataValidations.Where(r => r.Type == DvType.List)
            .Select(r => r.AppliesTo.ToString())
            .Should().BeEquivalentTo(["A1:A4", "A6:A10"],
                "cells outside the paste destination must keep their original validation");
        sheet.DataValidations.Should().Contain(r => r.Type == DvType.WholeNumber && r.AppliesTo.ToString() == "A5:A5");
        sheet.DataValidations.Should().Contain(r => r.Type == DvType.WholeNumber && r.AppliesTo.ToString() == "B1:B1",
            "the source rule being copied FROM must remain untouched");
    }

    [Fact]
    public void PasteDataValidationCommand_Apply_StillRemovesRuleFullyCoveredByDestination()
    {
        // Sibling no-regression check: when the existing rule's footprint is fully covered by the
        // paste destination (not merely overlapping part of a larger rule), it must still be fully
        // replaced, not spuriously preserved.
        var (wb, sheet) = MakeWorkbook();
        var targetRange = RangeAt(sheet, 5, 1); // A5
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = targetRange,
            Type = DvType.List,
            Formula1 = "Old",
        });
        var sourceRange = RangeAt(sheet, 1, 2); // B1
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = sourceRange,
            Type = DvType.WholeNumber,
            Operator = DvOperator.GreaterThanOrEqual,
            Formula1 = "1",
        });

        var bus = new CommandBus(_ => new TestCommandContext(wb));
        var destination = new CellAddress(sheet.Id, 5, 1); // A5
        bus.Execute(wb.Id, new PasteDataValidationCommand(sheet.Id, sourceRange, destination, transpose: false));

        sheet.DataValidations.Should().HaveCount(2,
            "the source rule at B1 (copied FROM) is untouched; only A5's fully-covered rule is replaced");
        sheet.DataValidations.Should().Contain(r => r.Type == DvType.WholeNumber && r.AppliesTo.ToString() == "A5:A5",
            "the old rule fully covered by the paste destination must be fully replaced");
        sheet.DataValidations.Should().Contain(r => r.Type == DvType.WholeNumber && r.AppliesTo.ToString() == "B1:B1");
    }

    [Fact]
    public void PasteDataValidationCommand_Apply_ClearsRuleReachedOnlyThroughAdditionalRanges()
    {
        var (wb, sheet) = MakeWorkbook();
        var rule = new DataValidation
        {
            AppliesTo = RangeAt(sheet, 1, 1), // A1
            Type = DvType.List,
            Formula1 = "Yes,No",
        };
        rule.AdditionalRanges.Add(RangeAt(sheet, 1, 3)); // C1
        sheet.DataValidations.Add(rule);

        var sourceRange = RangeAt(sheet, 1, 2); // B1
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = sourceRange,
            Type = DvType.WholeNumber,
            Operator = DvOperator.GreaterThanOrEqual,
            Formula1 = "1",
        });

        var bus = new CommandBus(_ => new TestCommandContext(wb));
        var destination = new CellAddress(sheet.Id, 1, 3); // C1 (only reachable via AdditionalRanges)
        bus.Execute(wb.Id, new PasteDataValidationCommand(sheet.Id, sourceRange, destination, transpose: false));

        sheet.DataValidations.Should().HaveCount(3,
            "the List rule survives only over A1, the untouched B1 source rule remains, and C1 gets the newly pasted rule");
        sheet.DataValidations.Should().Contain(r => r.Type == DvType.List && r.AppliesTo.ToString() == "A1:A1" && r.AdditionalRanges.Count == 0);
        sheet.DataValidations.Should().Contain(r => r.Type == DvType.WholeNumber && r.AppliesTo.ToString() == "C1:C1");
        sheet.DataValidations.Should().Contain(r => r.Type == DvType.WholeNumber && r.AppliesTo.ToString() == "B1:B1",
            "the source rule being copied FROM must remain untouched");
    }

    // ─── R52-commands-data-validation-apply-3-3: a new rule must supersede other overlapping rules ─

    [Fact]
    public void SetDataValidationCommand_Apply_ClearsDifferentlyAnchoredOverlappingRule()
    {
        var (wb, sheet) = MakeWorkbook();
        var existing = new DataValidation
        {
            AppliesTo = RangeAt(sheet, 1, 2), // B1
            Type = DvType.List,
            Formula1 = "Yes,No",
        };
        sheet.DataValidations.Add(existing);

        var newRule = new DataValidation
        {
            AppliesTo = RangeAt(sheet, 1, 1, 1, 3), // A1:C1 -- overlaps B1 but neither Id nor AppliesTo match
            Type = DvType.WholeNumber,
            Operator = DvOperator.GreaterThanOrEqual,
            Formula1 = "1",
        };

        var bus = new CommandBus(_ => new TestCommandContext(wb));
        bus.Execute(wb.Id, new SetDataValidationCommand(sheet.Id, newRule));

        sheet.DataValidations.Should().ContainSingle(
            "the new rule must fully supersede the old rule over every cell it covers, not merely be added alongside it")
            .Which.Type.Should().Be(DvType.WholeNumber);
    }

    [Fact]
    public void SetDataValidationCommand_Apply_StillReplacesRuleWithSameAppliesTo()
    {
        // Sibling no-regression check: the original exact-AppliesTo replace-in-place path must
        // keep working unchanged.
        var (wb, sheet) = MakeWorkbook();
        var range = RangeAt(sheet, 1, 1); // A1
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = range,
            Type = DvType.List,
            Formula1 = "A,B",
        });
        var replacement = new DataValidation
        {
            AppliesTo = range,
            Type = DvType.WholeNumber,
            Operator = DvOperator.GreaterThanOrEqual,
            Formula1 = "1",
        };

        var bus = new CommandBus(_ => new TestCommandContext(wb));
        bus.Execute(wb.Id, new SetDataValidationCommand(sheet.Id, replacement));

        sheet.DataValidations.Should().ContainSingle().Which.Type.Should().Be(DvType.WholeNumber);
    }

    [Fact]
    public void SetDataValidationCommand_Revert_RestoresOverlappingRuleClearedByApply()
    {
        var (wb, sheet) = MakeWorkbook();
        var existing = new DataValidation
        {
            AppliesTo = RangeAt(sheet, 1, 2), // B1
            Type = DvType.List,
            Formula1 = "Yes,No",
        };
        sheet.DataValidations.Add(existing);

        var newRule = new DataValidation
        {
            AppliesTo = RangeAt(sheet, 1, 1, 1, 3), // A1:C1
            Type = DvType.WholeNumber,
            Operator = DvOperator.GreaterThanOrEqual,
            Formula1 = "1",
        };

        var bus = new CommandBus(_ => new TestCommandContext(wb));
        bus.Execute(wb.Id, new SetDataValidationCommand(sheet.Id, newRule));
        bus.Undo(wb.Id);

        sheet.DataValidations.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(existing, options => options.Excluding(r => r.Id),
                "undo should restore the rule that Apply cleared because it overlapped the new rule's range");
    }
}
