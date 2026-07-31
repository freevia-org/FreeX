using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R107-paste-data-validation-1: a plain Ctrl+V (mode All, ContentKind Default, no Paste Special
/// options) never carried the copied source cell's data-validation rule to the destination --
/// PasteDataValidationCommand was only ever constructed from WorkbookSession's dedicated
/// PasteDataValidationFromClipboardAtActiveCell method, reached solely via the "Paste Special &gt;
/// Validation" dialog action (PasteSpecialAction.Validation), never from the ordinary mode==All
/// paste path that already generalizes to carry conditional formats, merged regions, comments,
/// pictures, shapes, textboxes, and charts along with a plain copy/paste (R91/R92/R96/R107-CF).
/// Real Excel always carries a cell's data-validation rule (e.g. a dropdown list) on a normal
/// Ctrl+V; "Paste Special &gt; Validation" is a narrower, distinct operation that copies JUST the
/// rule (no values/formats), not the mechanism by which validation travels at all.
/// </summary>
public sealed class R107_PlainPasteCarriesDataValidationTests
{
    private static DataValidation MakeRule(GridRange appliesTo) => new()
    {
        AppliesTo = appliesTo,
        Type = DvType.List,
        Formula1 = "\"A,B,C\""
    };

    /// <summary>
    /// The core failing-before-fix case: a single-cell, non-tiled, ordinary Ctrl+V (mode All,
    /// default Paste Special options -- i.e. no dialog opened at all) of a cell covered by a
    /// data-validation rule must create a pasted copy of that rule at the destination, exactly as
    /// it already does for conditional formats.
    /// </summary>
    [Fact]
    public void PlainPaste_NonTiled_CarriesDataValidationToDestination()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var sourceStart = new CellAddress(sheet.Id, 1, 1);
        var sourceRange = new GridRange(sourceStart, sourceStart);
        var sourceRule = MakeRule(sourceRange);
        sheet.DataValidations.Add(sourceRule);

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
        sheet.DataValidations.Should().HaveCount(2);
        var pastedRule = sheet.DataValidations.Single(rule => rule.Id != sourceRule.Id);
        pastedRule.AppliesTo.Should().Be(new GridRange(destinationStart, destinationStart));
        pastedRule.Formula1.Should().Be("\"A,B,C\"");

        command.Revert(ctx);

        sheet.GetCell(destinationStart).Should().BeNull();
        sheet.DataValidations.Should().ContainSingle(rule => rule.AppliesTo == sourceRange && rule.Formula1 == "\"A,B,C\"");
    }

    /// <summary>
    /// Tiled counterpart: a plain Ctrl+V of a single cell into a larger selected destination tiles
    /// the value across the whole destination (as it already did), and now must also paste the
    /// data-validation rule once, anchored at the destination's start -- mirroring the
    /// already-correct conditional-format tiled behavior.
    /// </summary>
    [Fact]
    public void PlainPaste_Tiled_CarriesDataValidationOnceAtDestinationStart()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var sourceStart = new CellAddress(sheet.Id, 1, 1);
        var sourceRange = new GridRange(sourceStart, sourceStart);
        var sourceRule = MakeRule(sourceRange);
        sheet.DataValidations.Add(sourceRule);

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

        sheet.DataValidations.Should().HaveCount(2);
        var pastedRule = sheet.DataValidations.Single(rule => rule.Id != sourceRule.Id);
        pastedRule.AppliesTo.Should().Be(new GridRange(destinationStart, destinationStart));

        command.Revert(ctx);

        sheet.GetCell(new CellAddress(sheet.Id, 1, 2)).Should().BeNull();
        sheet.DataValidations.Should().ContainSingle(rule => rule.AppliesTo == sourceRange && rule.Formula1 == "\"A,B,C\"");
    }

    /// <summary>
    /// No-regression sibling: a Values-only paste (mode==Values) must NOT carry the source's data
    /// validation -- values-only pastes deliberately strip all formatting/format-adjacent content
    /// (matching how they already strip rich-text runs/hyperlinks/merged regions/conditional
    /// formats), and this must remain true after the fix.
    /// </summary>
    [Fact]
    public void ValuesOnlyPaste_DoesNotCarryDataValidation()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var sourceStart = new CellAddress(sheet.Id, 1, 1);
        var sourceRange = new GridRange(sourceStart, sourceStart);
        sheet.DataValidations.Add(MakeRule(sourceRange));

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
        sheet.DataValidations.Should().HaveCount(1);
    }

    /// <summary>
    /// No-regression sibling: a source range with NO overlapping data-validation rule must not
    /// spuriously add a PasteDataValidationCommand / change the destination sheet's rule list at
    /// all.
    /// </summary>
    [Fact]
    public void PlainPaste_NoSourceDataValidation_DoesNotAddAnyRule()
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

        sheet.DataValidations.Should().BeEmpty();
    }
}
