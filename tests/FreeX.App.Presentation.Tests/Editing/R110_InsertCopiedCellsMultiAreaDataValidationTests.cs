using FluentAssertions;
using FreeX.App.Presentation.Editing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Editing;

/// <summary>
/// R110-insert-copied-cells-multiarea-1: r108 fixed the plain Ctrl+V path (ExecutePaste in
/// MainWindow.ClipboardCommands.cs) so a multi-area (Ctrl+click) copy's conditional-format/
/// data-validation carry only sweeps in rules that overlap an ACTUALLY-copied area, by forwarding
/// clip.SourceAreas into PasteCommandFactory.CreateInternalPasteCommand. The sibling right-click
/// action "Insert Copied Cells" (ExecuteInsertCopiedCells -> InsertCopiedCellsPlanner.CreateCommand)
/// was never updated: CreateCommand had no sourceAreas parameter at all, so its own
/// CreateInternalPasteCommand call always left sourceAreas at its default null, reproducing exactly
/// the bug r108 fixed for Ctrl+V in this sibling entry point. This mirrors
/// R108_PlainPasteMultiAreaDataValidationTests one layer up, through InsertCopiedCellsPlanner.
/// </summary>
public sealed class R110_InsertCopiedCellsMultiAreaDataValidationTests
{
    private static DataValidation MakeRule(GridRange appliesTo) => new()
    {
        AppliesTo = appliesTo,
        Type = DvType.List,
        Formula1 = "\"A,B,C\""
    };

    /// <summary>
    /// The core failing-before-fix case: a Ctrl+click multi-area copy of column 1 and column 3
    /// (sharing rows 1-2; bounding box spans columns 1-3) with a data-validation rule anchored ONLY
    /// in the untouched gap column (column 2 -- never part of either copied area) must NOT carry
    /// that rule to the destination when "Insert Copied Cells" is used. Before the fix,
    /// InsertCopiedCellsPlanner.CreateCommand had no way to forward clip.SourceAreas down to its
    /// PasteCommandFactory.CreateInternalPasteCommand call, so the gap rule's overlap with the whole
    /// bounding-box sourceRange caused it to be treated as "copied" and cloned onto the destination.
    /// </summary>
    [Fact]
    public void CreateCommand_MultiArea_ExcludesGapColumnDataValidation()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        var area1Start = new CellAddress(sheet.Id, 1, 1);
        var area1End = new CellAddress(sheet.Id, 2, 1);
        var area1 = new GridRange(area1Start, area1End);

        var area2Start = new CellAddress(sheet.Id, 1, 3);
        var area2End = new CellAddress(sheet.Id, 2, 3);
        var area2 = new GridRange(area2Start, area2End);

        var gapStart = new CellAddress(sheet.Id, 1, 2);
        var gapEnd = new CellAddress(sheet.Id, 2, 2);
        var gapRule = MakeRule(new GridRange(gapStart, gapEnd));
        sheet.DataValidations.Add(gapRule);

        var boundingSourceRange = new GridRange(area1Start, area2End);

        var cell1a = Cell.FromValue(new NumberValue(1));
        var cell1b = Cell.FromValue(new NumberValue(2));
        var cell2a = Cell.FromValue(new NumberValue(3));
        var cell2b = Cell.FromValue(new NumberValue(4));
        sheet.SetCell(area1Start, cell1a);
        sheet.SetCell(area1End, cell1b);
        sheet.SetCell(area2Start, cell2a);
        sheet.SetCell(area2End, cell2b);

        // Mirrors what MainWindow.ClipboardCommands.cs's copy handler populates clip.Cells with for
        // a multi-area copy -- only cells from the ACTUAL copied areas, never the gap.
        var clipCells = new[]
        {
            (area1Start, cell1a.Clone()),
            (area1End, cell1b.Clone()),
            (area2Start, cell2a.Clone()),
            (area2End, cell2b.Clone()),
        };

        var destinationAnchor = new CellAddress(sheet.Id, 10, 10);
        var sourceAreas = new List<GridRange> { area1, area2 };

        var command = InsertCopiedCellsPlanner.CreateCommand(
            workbook,
            sheet.Id,
            boundingSourceRange,
            clipCells,
            new GridRange(destinationAnchor, destinationAnchor),
            KeyboardInsertDeleteDialogChoice.ShiftRight,
            isCut: false,
            sourceAreas: sourceAreas);

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        // Only the original gap rule should exist -- nothing pasted onto the destination anywhere,
        // and in particular nothing at the destination column corresponding to the untouched gap.
        sheet.DataValidations.Should().ContainSingle();
        sheet.DataValidations[0].Id.Should().Be(gapRule.Id);
    }

    /// <summary>
    /// No-regression sibling: a rule anchored inside one of the ACTUAL copied areas (not the gap)
    /// must still be carried to the destination by "Insert Copied Cells" on a multi-area source,
    /// proving the sourceAreas filtering only suppresses gap-only overlaps and does not regress
    /// genuine multi-area data-validation carrying.
    /// </summary>
    [Fact]
    public void CreateCommand_MultiArea_StillCarriesRuleInsideCopiedArea()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        var area1Start = new CellAddress(sheet.Id, 1, 1);
        var area1End = new CellAddress(sheet.Id, 2, 1);
        var area1 = new GridRange(area1Start, area1End);

        var area2Start = new CellAddress(sheet.Id, 1, 3);
        var area2End = new CellAddress(sheet.Id, 2, 3);
        var area2 = new GridRange(area2Start, area2End);

        // The rule is anchored directly in area1 -- an actual copied area, not the gap.
        var sourceRule = MakeRule(area1);
        sheet.DataValidations.Add(sourceRule);

        var boundingSourceRange = new GridRange(area1Start, area2End);

        var cell1a = Cell.FromValue(new NumberValue(1));
        var cell1b = Cell.FromValue(new NumberValue(2));
        var cell2a = Cell.FromValue(new NumberValue(3));
        var cell2b = Cell.FromValue(new NumberValue(4));
        sheet.SetCell(area1Start, cell1a);
        sheet.SetCell(area1End, cell1b);
        sheet.SetCell(area2Start, cell2a);
        sheet.SetCell(area2End, cell2b);

        var clipCells = new[]
        {
            (area1Start, cell1a.Clone()),
            (area1End, cell1b.Clone()),
            (area2Start, cell2a.Clone()),
            (area2End, cell2b.Clone()),
        };

        var destinationAnchor = new CellAddress(sheet.Id, 10, 10);
        var sourceAreas = new List<GridRange> { area1, area2 };

        var command = InsertCopiedCellsPlanner.CreateCommand(
            workbook,
            sheet.Id,
            boundingSourceRange,
            clipCells,
            new GridRange(destinationAnchor, destinationAnchor),
            KeyboardInsertDeleteDialogChoice.ShiftRight,
            isCut: false,
            sourceAreas: sourceAreas);

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        sheet.DataValidations.Should().HaveCount(2);
        var pastedRule = sheet.DataValidations.Single(rule => rule.Id != sourceRule.Id);
        // area1 sits at the bounding box's own top-left corner (column offset 0), so it lands
        // exactly at the destination anchor.
        pastedRule.AppliesTo.Should().Be(new GridRange(destinationAnchor, new CellAddress(sheet.Id, destinationAnchor.Row + 1, destinationAnchor.Col)));
        pastedRule.Formula1.Should().Be("\"A,B,C\"");
    }

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }
}
