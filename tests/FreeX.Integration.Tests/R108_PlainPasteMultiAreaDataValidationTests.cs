using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R108-paste-datavalidation-multiarea-1: r107's plain-Ctrl+V data-validation carry logic
/// (PasteCommandFactory.CreateInternalPasteCommand / CreateTiledInternalPasteCommand) constructed
/// its PasteDataValidationCommand from the paste's bounding-box sourceRange only, with no way to
/// forward a multi-area (Ctrl+click) clipboard's individual sourceAreas -- CreateInternalPasteCommand
/// had no sourceAreas parameter at all. PasteDataValidationCommand's own sourceAreas constructor
/// parameter (R78-commands-paste-special-5-4) exists specifically to prevent a rule anchored purely
/// in the untouched GAP between disjoint Ctrl+click copied areas from being swept in by the
/// bounding-box intersection; without a way to supply it, a plain Ctrl+V of a multi-area selection
/// would still paste a gap-only rule it never actually copied. This mirrors the already-correct
/// wiring at WorkbookSession.cs/MainWindow.ClipboardCommands.cs for the dedicated Paste-Special-
/// Validation and Format-Painter call sites (R78-commands-paste-special-5-1/-3/-4).
/// </summary>
public sealed class R108_PlainPasteMultiAreaDataValidationTests
{
    private static DataValidation MakeRule(GridRange appliesTo) => new()
    {
        AppliesTo = appliesTo,
        Type = DvType.List,
        Formula1 = "\"A,B,C\""
    };

    /// <summary>
    /// The core failing-before-fix case: a Ctrl+click multi-area copy of row1,col1 and row3,col1
    /// (bounding box spans rows 1-3) with a data-validation rule anchored ONLY in the untouched gap
    /// cell (row2,col1 -- never part of either copied area) must NOT paste that rule to the
    /// destination on a plain Ctrl+V. Before the fix, CreateInternalPasteCommand had no way to
    /// forward clip.SourceAreas down to the r107 data-validation carry call site, so the gap rule's
    /// overlap with the whole bounding-box sourceRange caused it to be treated as "copied" and
    /// cloned onto the destination.
    /// </summary>
    [Fact]
    public void PlainPaste_NonTiled_MultiArea_ExcludesGapCellDataValidation()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var area1Cell = new CellAddress(sheet.Id, 1, 1);
        var gapCell = new CellAddress(sheet.Id, 2, 1);
        var area2Cell = new CellAddress(sheet.Id, 3, 1);
        var area1 = new GridRange(area1Cell, area1Cell);
        var area2 = new GridRange(area2Cell, area2Cell);
        var boundingSourceRange = new GridRange(area1Cell, area2Cell);

        // A rule anchored purely in the gap between the two Ctrl+clicked areas -- never selected or
        // copied.
        var gapRule = MakeRule(new GridRange(gapCell, gapCell));
        sheet.DataValidations.Add(gapRule);

        var cell1 = Cell.FromValue(new NumberValue(1));
        var cell2 = Cell.FromValue(new NumberValue(3));
        sheet.SetCell(area1Cell, cell1);
        sheet.SetCell(area2Cell, cell2);

        var destinationStart = new CellAddress(sheet.Id, 10, 1);
        var sourceAreas = new List<GridRange> { area1, area2 };

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            boundingSourceRange,
            [(area1Cell, cell1.Clone()), (area2Cell, cell2.Clone())],
            destinationStart,
            PasteCellsMode.All,
            new PasteSpecialOptions(),
            sourceAreas);

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        // Only the original gap rule should exist -- nothing pasted onto the destination.
        sheet.DataValidations.Should().ContainSingle();
        sheet.DataValidations[0].Id.Should().Be(gapRule.Id);
    }

    /// <summary>
    /// Tiled counterpart of the non-tiled case above: a plain Ctrl+V of the same disjoint multi-area
    /// copy onto a larger (whole-multiple) destination selection tiles the values as before, and
    /// still must not carry the gap-only rule -- covering CreateTiledInternalPasteCommand's own
    /// PasteDataValidationCommand construction site.
    /// </summary>
    [Fact]
    public void PlainPaste_Tiled_MultiArea_ExcludesGapCellDataValidation()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var area1Cell = new CellAddress(sheet.Id, 1, 1);
        var gapCell = new CellAddress(sheet.Id, 2, 1);
        var area2Cell = new CellAddress(sheet.Id, 3, 1);
        var area1 = new GridRange(area1Cell, area1Cell);
        var area2 = new GridRange(area2Cell, area2Cell);
        var boundingSourceRange = new GridRange(area1Cell, area2Cell);

        var gapRule = MakeRule(new GridRange(gapCell, gapCell));
        sheet.DataValidations.Add(gapRule);

        var cell1 = Cell.FromValue(new NumberValue(1));
        var cell2 = Cell.FromValue(new NumberValue(3));
        sheet.SetCell(area1Cell, cell1);
        sheet.SetCell(area2Cell, cell2);

        // 6-row destination selection = exactly 2 whole tiles of the 3-row bounding source range.
        var destinationRange = new GridRange(new CellAddress(sheet.Id, 10, 1), new CellAddress(sheet.Id, 15, 1));
        var sourceAreas = new List<GridRange> { area1, area2 };

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            boundingSourceRange,
            [(area1Cell, cell1.Clone()), (area2Cell, cell2.Clone())],
            destinationRange,
            PasteCellsMode.All,
            new PasteSpecialOptions(),
            sourceAreas);

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        sheet.DataValidations.Should().ContainSingle();
        sheet.DataValidations[0].Id.Should().Be(gapRule.Id);
    }

    /// <summary>
    /// No-regression sibling: a rule anchored inside one of the ACTUAL copied areas (not the gap)
    /// must still be carried to the destination on a plain multi-area Ctrl+V, proving the sourceAreas
    /// filtering only suppresses gap-only overlaps and does not regress genuine multi-area
    /// data-validation carrying.
    /// </summary>
    [Fact]
    public void PlainPaste_NonTiled_MultiArea_StillCarriesRuleInsideCopiedArea()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var area1Cell = new CellAddress(sheet.Id, 1, 1);
        var area2Cell = new CellAddress(sheet.Id, 3, 1);
        var area1 = new GridRange(area1Cell, area1Cell);
        var area2 = new GridRange(area2Cell, area2Cell);
        var boundingSourceRange = new GridRange(area1Cell, area2Cell);

        // The rule is anchored directly in area1 -- an actual copied cell, not the gap.
        var sourceRule = MakeRule(area1);
        sheet.DataValidations.Add(sourceRule);

        var cell1 = Cell.FromValue(new NumberValue(1));
        var cell2 = Cell.FromValue(new NumberValue(3));
        sheet.SetCell(area1Cell, cell1);
        sheet.SetCell(area2Cell, cell2);

        var destinationStart = new CellAddress(sheet.Id, 10, 1);
        var sourceAreas = new List<GridRange> { area1, area2 };

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            boundingSourceRange,
            [(area1Cell, cell1.Clone()), (area2Cell, cell2.Clone())],
            destinationStart,
            PasteCellsMode.All,
            new PasteSpecialOptions(),
            sourceAreas);

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        sheet.DataValidations.Should().HaveCount(2);
        var pastedRule = sheet.DataValidations.Single(rule => rule.Id != sourceRule.Id);
        pastedRule.AppliesTo.Should().Be(new GridRange(destinationStart, destinationStart));
        pastedRule.Formula1.Should().Be("\"A,B,C\"");
    }
}
