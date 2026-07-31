using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R107-paste-conditional-formats-1: a plain Ctrl+V (mode All, ContentKind Default, no Paste
/// Special options) never carried the copied source cell's conditional-format rule to the
/// destination -- PasteConditionalFormatsCommand was only ever invoked from the dedicated
/// ContentKind==AllMergingConditionalFormats branches (both tiled and non-tiled), never from the
/// ordinary mode==All/Default paste path that already generalizes to carry merged regions,
/// comments, pictures, shapes, textboxes, and charts along with a plain copy/paste
/// (R91/R92/R96). Real Excel always carries a cell's conditional formatting on a normal Ctrl+V;
/// "Paste Special > All merging conditional formats" is a narrower, distinct operation that ADDS
/// to whatever CF already sits at the destination, not the mechanism by which CF travels at all.
/// </summary>
public sealed class R107_PlainPasteCarriesConditionalFormatsTests
{
    private static ConditionalFormat MakeRule(GridRange appliesTo, int priority = 1) => new()
    {
        AppliesTo = appliesTo,
        RuleType = CfRuleType.CellValue,
        Operator = CfOperator.GreaterThan,
        Value1 = "10",
        FormatIfTrue = new CellStyle { Bold = true },
        Priority = priority
    };

    /// <summary>
    /// The core failing-before-fix case: a single-cell, non-tiled, ordinary Ctrl+V (mode All,
    /// default Paste Special options -- i.e. no dialog opened at all) of a cell covered by a
    /// conditional-format rule must create a pasted copy of that rule at the destination, exactly
    /// as it already does for "Paste Special > All merging conditional formats".
    /// </summary>
    [Fact]
    public void PlainPaste_NonTiled_CarriesConditionalFormatToDestination()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var sourceStart = new CellAddress(sheet.Id, 1, 1);
        var sourceRange = new GridRange(sourceStart, sourceStart);
        var sourceRule = MakeRule(sourceRange);
        sheet.ConditionalFormats.Add(sourceRule);

        var sourceCell = Cell.FromValue(new NumberValue(42));
        sheet.SetCell(sourceStart, sourceCell);

        var destinationStart = new CellAddress(sheet.Id, 5, 5);
        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            sourceRange,
            [(sourceStart, sourceCell.Clone())],
            destinationStart,
            PasteCellsMode.All,
            new PasteSpecialOptions());

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        sheet.GetValue(destinationStart).Should().Be(new NumberValue(42));
        sheet.ConditionalFormats.Should().HaveCount(2);
        var pastedRule = sheet.ConditionalFormats.Single(rule => rule.Id != sourceRule.Id);
        pastedRule.AppliesTo.Should().Be(new GridRange(destinationStart, destinationStart));
        pastedRule.Value1.Should().Be("10");

        command.Revert(ctx);

        sheet.GetCell(destinationStart).Should().BeNull();
        sheet.ConditionalFormats.Should().Equal(sourceRule);
    }

    /// <summary>
    /// Tiled counterpart: a plain Ctrl+V of a single cell into a larger selected destination
    /// tiles the value across the whole destination (as it already did), and now must also paste
    /// the conditional-format rule once, anchored at the destination's start -- mirroring the
    /// already-correct AllMergingConditionalFormats tiled behavior (R25-clipboard-paste-remaining-2).
    /// </summary>
    [Fact]
    public void PlainPaste_Tiled_CarriesConditionalFormatOnceAtDestinationStart()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var sourceStart = new CellAddress(sheet.Id, 1, 1);
        var sourceRange = new GridRange(sourceStart, sourceStart);
        var sourceRule = MakeRule(sourceRange);
        sheet.ConditionalFormats.Add(sourceRule);

        var sourceCell = Cell.FromValue(new NumberValue(42));
        sheet.SetCell(sourceStart, sourceCell);

        var destinationStart = new CellAddress(sheet.Id, 1, 2);
        var destinationRange = new GridRange(destinationStart, new CellAddress(sheet.Id, 3, 2));

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            sourceRange,
            [(sourceStart, sourceCell.Clone())],
            destinationRange,
            PasteCellsMode.All,
            new PasteSpecialOptions());

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new NumberValue(42));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 2)).Should().Be(new NumberValue(42));
        sheet.GetValue(new CellAddress(sheet.Id, 3, 2)).Should().Be(new NumberValue(42));

        sheet.ConditionalFormats.Should().HaveCount(2);
        var pastedRule = sheet.ConditionalFormats.Single(rule => rule.Id != sourceRule.Id);
        pastedRule.AppliesTo.Should().Be(new GridRange(destinationStart, destinationStart));

        command.Revert(ctx);

        sheet.GetCell(new CellAddress(sheet.Id, 1, 2)).Should().BeNull();
        sheet.ConditionalFormats.Should().Equal(sourceRule);
    }

    /// <summary>
    /// No-regression sibling: a Values-only paste (mode==Values) must NOT carry the source's
    /// conditional formatting -- values-only pastes deliberately strip all formatting (matching
    /// how they already strip rich-text runs/hyperlinks/merged regions), and this must remain true
    /// after the fix.
    /// </summary>
    [Fact]
    public void ValuesOnlyPaste_DoesNotCarryConditionalFormat()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var sourceStart = new CellAddress(sheet.Id, 1, 1);
        var sourceRange = new GridRange(sourceStart, sourceStart);
        sheet.ConditionalFormats.Add(MakeRule(sourceRange));

        var sourceCell = Cell.FromValue(new NumberValue(42));
        sheet.SetCell(sourceStart, sourceCell);

        var destinationStart = new CellAddress(sheet.Id, 5, 5);
        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            sourceRange,
            [(sourceStart, sourceCell.Clone())],
            destinationStart,
            PasteCellsMode.Values,
            new PasteSpecialOptions());

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        sheet.GetValue(destinationStart).Should().Be(new NumberValue(42));
        sheet.ConditionalFormats.Should().HaveCount(1);
    }

    /// <summary>
    /// No-regression sibling: a source range with NO overlapping conditional-format rule must not
    /// spuriously add a PasteConditionalFormatsCommand / change the destination sheet's rule list
    /// at all.
    /// </summary>
    [Fact]
    public void PlainPaste_NoSourceConditionalFormat_DoesNotAddAnyRule()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var sourceStart = new CellAddress(sheet.Id, 1, 1);
        var sourceRange = new GridRange(sourceStart, sourceStart);
        var sourceCell = Cell.FromValue(new NumberValue(42));
        sheet.SetCell(sourceStart, sourceCell);

        var destinationStart = new CellAddress(sheet.Id, 5, 5);
        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            sourceRange,
            [(sourceStart, sourceCell.Clone())],
            destinationStart,
            PasteCellsMode.All,
            new PasteSpecialOptions());

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        sheet.ConditionalFormats.Should().BeEmpty();
    }
}
