using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R150 spill-overlay-root F12 + data-validation F1: AutofillCommand.cs.
/// </summary>
public sealed class R150_AutofillSpillSeriesAndDataValidationCarryTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new TestCommandContext(wb));
    }

    // ── F12: numeric spill source range must fit a trend, not clear/copy ──────────────────────

    [Fact]
    public void FillDown_SourceRangeIsNumericSpill_ContinuesLinearTrend()
    {
        var (_, sheet, ctx) = Setup();

        // B2 is the anchor of a spilled formula {1;2;3;4}; B3:B5 are non-anchor spill members
        // that live only in the sheet's separate spill overlay (no _cells entry of their own).
        var anchor = new CellAddress(sheet.Id, 2, 2); // B2
        sheet.SetFormula(anchor, "{1;2;3;4}");
        sheet.GetCell(anchor)!.Value = new NumberValue(1);
        sheet.SetSpillRange(anchor, new RangeValue(new ScalarValue[4, 1]
        {
            { new NumberValue(1) }, // anchor slot (ignored by SetSpillRange)
            { new NumberValue(2) }, // B3
            { new NumberValue(3) }, // B4
            { new NumberValue(4) }, // B5
        }));

        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 5, 2)); // B2:B5
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 6, 2),
            new CellAddress(sheet.Id, 9, 2)); // B6:B9

        var outcome = new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GetValue(6, 2).Should().Be(new NumberValue(5), "the trend 1,2,3,4 must continue to 5");
        sheet.GetValue(7, 2).Should().Be(new NumberValue(6));
        sheet.GetValue(8, 2).Should().Be(new NumberValue(7));
        sheet.GetValue(9, 2).Should().Be(new NumberValue(8));
    }

    // ── Sibling no-regression: an ordinary (non-spill) numeric source still trends correctly ──

    [Fact]
    public void FillDown_SourceRangeIsOrdinaryNumbers_StillContinuesLinearTrend()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(3));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(4));

        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 5, 2));
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 6, 2),
            new CellAddress(sheet.Id, 9, 2));

        new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        sheet.GetValue(6, 2).Should().Be(new NumberValue(5));
        sheet.GetValue(9, 2).Should().Be(new NumberValue(8));
    }

    // ── Sibling no-regression: a genuinely blank source cell mixed in still refuses a trend ───

    [Fact]
    public void FillDown_SourceRangeHasGenuineBlankCell_DoesNotFitTrend()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(1));
        // Row 3 (B3) intentionally left blank -- no Cell, no spill.
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(3));

        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 4, 2));
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 5, 2),
            new CellAddress(sheet.Id, 5, 2));

        new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        // Must NOT be a fitted-trend value (4): a genuinely blank source cell must still block
        // scalar-series detection exactly as before this fix.
        sheet.GetValue(5, 2).Should().NotBe(new NumberValue(4));
    }

    // ── F1: fill handle drag must carry a List/dropdown validation rule to filled cells ───────

    [Fact]
    public void FillDown_SourceCellHasListValidation_CarriesRuleToFilledCells()
    {
        var (_, sheet, ctx) = Setup();
        var source = new CellAddress(sheet.Id, 1, 1); // A1
        sheet.SetCell(source, new TextValue("Yes"));
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(source, source),
            Type = DvType.List,
            Operator = DvOperator.Between,
            Formula1 = "\"Yes,No\"",
        });

        var sourceRange = new GridRange(source, source);
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 3, 1)); // A2:A3

        var outcome = new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GetValue(2, 1).Should().Be(new TextValue("Yes"));

        var a2 = new CellAddress(sheet.Id, 2, 1);
        var a3 = new CellAddress(sheet.Id, 3, 1);
        sheet.DataValidations.Any(rule => rule.AppliesTo.Contains(a2)).Should().BeTrue(
            "dragging the fill handle over a validated cell must extend the rule, like Ctrl+V does");
        sheet.DataValidations.Any(rule => rule.AppliesTo.Contains(a3)).Should().BeTrue();
    }

    // ── Undo must restore exactly the validation state from before the fill ───────────────────

    [Fact]
    public void FillDown_SourceCellHasListValidation_UndoRestoresOriginalValidationState()
    {
        var (_, sheet, ctx) = Setup();
        var source = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(source, new TextValue("Yes"));
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(source, source),
            Type = DvType.List,
            Operator = DvOperator.Between,
            Formula1 = "\"Yes,No\"",
        });

        var beforeCount = sheet.DataValidations.Count;

        var sourceRange = new GridRange(source, source);
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 3, 1));

        var command = new AutofillCommand(sheet.Id, sourceRange, fillRange);
        command.Apply(ctx);
        sheet.DataValidations.Count.Should().BeGreaterThan(beforeCount);

        command.Revert(ctx);

        sheet.DataValidations.Should().HaveCount(beforeCount);
        sheet.DataValidations[0].AppliesTo.Should().Be(new GridRange(source, source));
    }

    // ── Sibling no-regression: filling from a cell with NO validation over a cell that HAD one
    // clears the destination's stale rule, matching Ctrl+V/PasteDataValidationCommand's R137
    // "clear even when source has none" contract. ─────────────────────────────────────────────

    [Fact]
    public void FillDown_SourceHasNoValidation_ClearsPreExistingDestinationRule()
    {
        var (_, sheet, ctx) = Setup();
        var source = new CellAddress(sheet.Id, 1, 1); // A1, no validation
        sheet.SetCell(source, new NumberValue(1));

        var dest = new CellAddress(sheet.Id, 2, 1); // A2 already has an unrelated rule
        sheet.SetCell(dest, new TextValue("Old"));
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(dest, dest),
            Type = DvType.List,
            Operator = DvOperator.Between,
            Formula1 = "\"Old,Other\"",
        });

        var sourceRange = new GridRange(source, source);
        var fillRange = new GridRange(dest, dest);

        new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        sheet.DataValidations.Any(rule => rule.AppliesTo.Contains(dest)).Should().BeFalse(
            "a fill from an unvalidated source must supersede the destination's stale rule, matching Ctrl+V");
    }
}
