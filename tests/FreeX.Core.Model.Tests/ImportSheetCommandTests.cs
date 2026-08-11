using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class ImportSheetCommandTests
{
    [Fact]
    public void ImportSheetCommand_CopiesUsedCellsToDestinationAndUndoRestores()
    {
        var targetWorkbook = new Workbook("target");
        var targetSheet = targetWorkbook.AddSheet("Sheet1");
        var sourceWorkbook = new Workbook("source");
        var sourceSheet = sourceWorkbook.AddSheet("Imported");
        sourceSheet.SetCell(new CellAddress(sourceSheet.Id, 1, 1), new NumberValue(10));
        sourceSheet.SetCell(new CellAddress(sourceSheet.Id, 2, 2), new TextValue("hello"));
        var destination = new CellAddress(targetSheet.Id, 3, 3);
        targetSheet.SetCell(destination, new NumberValue(999));
        var ctx = new TestCommandContext(targetWorkbook);

        var command = new ImportSheetCommand(targetSheet.Id, destination, sourceSheet);

        command.Apply(ctx).Success.Should().BeTrue();

        targetSheet.GetValue(3, 3).Should().Be(new NumberValue(10));
        targetSheet.GetValue(4, 4).Should().Be(new TextValue("hello"));

        command.Revert(ctx);

        targetSheet.GetValue(3, 3).Should().Be(new NumberValue(999));
        targetSheet.GetCell(4, 4).Should().BeNull();
    }

    [Fact]
    public void ImportSheetCommand_RejectsDestinationExtentPastWorksheetEdge()
    {
        var targetWorkbook = new Workbook("target");
        var targetSheet = targetWorkbook.AddSheet("Sheet1");
        var sourceWorkbook = new Workbook("source");
        var sourceSheet = sourceWorkbook.AddSheet("Imported");
        sourceSheet.SetCell(new CellAddress(sourceSheet.Id, 1, 1), new NumberValue(10));
        sourceSheet.SetCell(new CellAddress(sourceSheet.Id, 1, 2), new NumberValue(20));
        var destination = new CellAddress(targetSheet.Id, 1, CellAddress.MaxCol);
        targetSheet.SetCell(destination, new TextValue("keep"));
        var ctx = new TestCommandContext(targetWorkbook);

        var command = new ImportSheetCommand(targetSheet.Id, destination, sourceSheet);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("bounds");
        targetSheet.GetValue(destination).Should().Be(new TextValue("keep"));
    }

    [Fact]
    public void ImportSheetCommand_UndoRestoresStyleOnlyDestination()
    {
        var targetWorkbook = new Workbook("target");
        var targetSheet = targetWorkbook.AddSheet("Sheet1");
        var styleId = targetWorkbook.RegisterStyle(new CellStyle { Italic = true });
        var destination = new CellAddress(targetSheet.Id, 3, 3);
        targetSheet.SetStyleOnly(destination.Row, destination.Col, styleId);
        var sourceWorkbook = new Workbook("source");
        var sourceSheet = sourceWorkbook.AddSheet("Imported");
        sourceSheet.SetCell(new CellAddress(sourceSheet.Id, 1, 1), new NumberValue(10));
        var ctx = new TestCommandContext(targetWorkbook);

        var command = new ImportSheetCommand(targetSheet.Id, destination, sourceSheet);

        command.Apply(ctx).Success.Should().BeTrue();
        targetSheet.GetCell(destination)!.StyleId.Should().Be(styleId);

        command.Revert(ctx);

        targetSheet.GetCell(destination).Should().BeNull();
        targetSheet.GetStyleOnly(destination.Row, destination.Col).Should().Be(styleId);
    }

    [Fact]
    public void ImportSheetCommand_RejectsImportIntoProtectedLockedCells()
    {
        var targetWorkbook = new Workbook("target");
        var targetSheet = targetWorkbook.AddSheet("Sheet1");
        var sourceWorkbook = new Workbook("source");
        var sourceSheet = sourceWorkbook.AddSheet("Imported");
        sourceSheet.SetCell(new CellAddress(sourceSheet.Id, 1, 1), new NumberValue(10));
        targetSheet.IsProtected = true;
        var ctx = new TestCommandContext(targetWorkbook);

        var command = new ImportSheetCommand(targetSheet.Id, new CellAddress(targetSheet.Id, 1, 1), sourceSheet);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        targetSheet.GetCell(1, 1).Should().BeNull();
    }

    /// <summary>
    /// Round 134 fix: Data ▸ Refresh All (and any other caller that hands ImportSheetCommand the
    /// PRIOR import's extent) must clear cells the earlier, larger import wrote once the refreshed
    /// source has shrunk -- otherwise those cells keep the stale value and read as if they were still
    /// part of the current import. Builds a 10-row x 5-col source, imports it, then re-imports a
    /// shrunk 6-row x 3-col source at the same anchor with the original extent passed as
    /// previousExtent (mirroring what the Avalonia GetData refresh path now does), and asserts rows
    /// 7-10 and columns 4-5 of the original block are empty afterward.
    /// </summary>
    [Fact]
    public void ImportSheetCommand_RefreshWithShrunkSourceClearsLeftoverCells()
    {
        var targetWorkbook = new Workbook("target");
        var targetSheet = targetWorkbook.AddSheet("Sheet1");
        var destination = new CellAddress(targetSheet.Id, 1, 1);
        var ctx = new TestCommandContext(targetWorkbook);

        // First import: a full 10x5 block of numbers.
        var firstSourceWorkbook = new Workbook("source1");
        var firstSourceSheet = firstSourceWorkbook.AddSheet("Imported");
        for (uint r = 0; r < 10; r++)
        {
            for (uint c = 0; c < 5; c++)
                firstSourceSheet.SetCell(new CellAddress(firstSourceSheet.Id, 1 + r, 1 + c), new NumberValue(100 + r * 5 + c));
        }

        var firstCommand = new ImportSheetCommand(targetSheet.Id, destination, firstSourceSheet);
        firstCommand.Apply(ctx).Success.Should().BeTrue();

        // Sanity: the full 10x5 block landed.
        targetSheet.GetValue(10, 5).Should().Be(new NumberValue(100 + 9 * 5 + 4));

        // Second import ("refresh"): the same source shrunk to 6x3, with the previous 10x5 extent
        // passed through so the command knows what to reconcile against.
        var secondSourceWorkbook = new Workbook("source2");
        var secondSourceSheet = secondSourceWorkbook.AddSheet("Imported");
        for (uint r = 0; r < 6; r++)
        {
            for (uint c = 0; c < 3; c++)
                secondSourceSheet.SetCell(new CellAddress(secondSourceSheet.Id, 1 + r, 1 + c), new NumberValue(200 + r * 3 + c));
        }

        var refreshCommand = new ImportSheetCommand(targetSheet.Id, destination, secondSourceSheet, (RowCount: 10u, ColCount: 5u));
        var outcome = refreshCommand.Apply(ctx);
        outcome.Success.Should().BeTrue();

        // The new 6x3 block is present.
        for (uint r = 0; r < 6; r++)
        {
            for (uint c = 0; c < 3; c++)
                targetSheet.GetValue(1 + r, 1 + c).Should().Be(new NumberValue(200 + r * 3 + c));
        }

        // Rows 7-10 (within the old 5-column width) must be cleared, not left with the stale import.
        for (uint r = 7; r <= 10; r++)
        {
            for (uint c = 1; c <= 5; c++)
                targetSheet.GetCell(r, c).Should().BeNull($"row {r} col {c} is outside the shrunk 6x3 import and should be cleared");
        }

        // Columns 4-5 (within the rows the new import still covers) must also be cleared.
        for (uint r = 1; r <= 10; r++)
        {
            for (uint c = 4; c <= 5; c++)
                targetSheet.GetCell(r, c).Should().BeNull($"row {r} col {c} is outside the shrunk 6x3 import and should be cleared");
        }

        // Undo must restore the FIRST import's full 10x5 block exactly (both the shrink-refresh's
        // overwrites and its leftover-cell clears roll back).
        refreshCommand.Revert(ctx);
        for (uint r = 0; r < 10; r++)
        {
            for (uint c = 0; c < 5; c++)
                targetSheet.GetValue(1 + r, 1 + c).Should().Be(new NumberValue(100 + r * 5 + c));
        }
    }

    /// <summary>
    /// Sibling no-regression: when the refreshed source is the SAME size (or grows), there is no
    /// leftover rectangle to reconcile, so previousExtent must not clear anything it shouldn't --
    /// only the plain overwrite happens, exactly like a previousExtent-less import.
    /// </summary>
    [Fact]
    public void ImportSheetCommand_RefreshWithSameSizeSourceClearsNothingExtra()
    {
        var targetWorkbook = new Workbook("target");
        var targetSheet = targetWorkbook.AddSheet("Sheet1");
        var destination = new CellAddress(targetSheet.Id, 1, 1);
        var ctx = new TestCommandContext(targetWorkbook);

        var sourceWorkbook = new Workbook("source");
        var sourceSheet = sourceWorkbook.AddSheet("Imported");
        sourceSheet.SetCell(new CellAddress(sourceSheet.Id, 1, 1), new NumberValue(1));
        sourceSheet.SetCell(new CellAddress(sourceSheet.Id, 2, 2), new NumberValue(2));

        // A neighboring cell just outside the 2x2 import extent that the user owns -- must survive.
        targetSheet.SetCell(new CellAddress(targetSheet.Id, 3, 3), new TextValue("user content"));

        var command = new ImportSheetCommand(targetSheet.Id, destination, sourceSheet, (RowCount: 2u, ColCount: 2u));
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        targetSheet.GetValue(1, 1).Should().Be(new NumberValue(1));
        targetSheet.GetValue(2, 2).Should().Be(new NumberValue(2));
        targetSheet.GetValue(3, 3).Should().Be(new TextValue("user content"));
    }
}
