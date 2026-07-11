using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R22-protection-security-2: a default Paste (Ctrl+V, PasteCellsMode.All) onto a protected sheet's
/// UNLOCKED cell used to only check CommandGuards.CanEditCell (the per-cell Locked-flag check) before
/// unconditionally overwriting the destination's Cell.StyleId with the source's formatting via
/// PasteCellsCommand. That let a user route around the Format Cells sheet-protection permission simply
/// by pasting instead of using the Format Cells dialog/ribbon Bold button (which DOES call
/// ApplyStyleCommand -> CommandGuards.RejectIfProtectedWithoutPermission(..., FormatCells) and
/// correctly rejects). PasteCellsCommand must reject a formatting-carrying paste the same way, while a
/// value-only paste (Paste Special > Values) must remain allowed on an explicitly unlocked cell.
/// </summary>
public sealed class R22_PasteCellsProtectionTests
{
    private static (Workbook Workbook, Sheet Sheet, TestCommandContext Ctx, CellAddress Source, CellAddress Destination) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 2, 1);

        var sourceStyle = wb.RegisterStyle(new CellStyle { Bold = true, FillColor = new CellColor(255, 0, 0) });
        var sourceCell = Cell.FromValue(new NumberValue(42));
        sourceCell.StyleId = sourceStyle;
        sheet.SetCell(source, sourceCell);

        // Destination cell is explicitly UNLOCKED, so a direct value edit would be allowed even while
        // the sheet is protected -- CanEditCell alone must not be enough to greenlight a formatting
        // change; FormatCells permission is still required for that.
        var destinationStyle = wb.RegisterStyle(new CellStyle { Italic = true, Locked = false });
        var destinationCell = Cell.FromValue(new TextValue("old"));
        destinationCell.StyleId = destinationStyle;
        sheet.SetCell(destination, destinationCell);

        sheet.IsProtected = true;
        // FormatCells is deliberately NOT granted.

        return (wb, sheet, ctx, source, destination);
    }

    [Fact]
    public void PasteCommandFactory_AllModeRejectsProtectedSheetWithoutFormatCellsPermission_EvenOnUnlockedCell()
    {
        var (wb, sheet, ctx, source, destination) = Setup();
        var sourceCell = sheet.GetCell(source)!;
        var originalDestinationCell = sheet.GetCell(destination)!.Clone();

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(source, source),
            [(source, sourceCell.Clone())],
            destination,
            PasteCellsMode.All,
            default);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");

        var destinationCell = sheet.GetCell(destination)!;
        destinationCell.Value.Should().Be(originalDestinationCell.Value);
        destinationCell.StyleId.Should().Be(originalDestinationCell.StyleId);
    }

    [Fact]
    public void PasteCommandFactory_ValuesModeStillAllowedOnProtectedSheetWithoutFormatCellsPermission_WhenCellUnlocked()
    {
        var (wb, sheet, ctx, source, destination) = Setup();
        var sourceCell = sheet.GetCell(source)!;
        var originalDestinationStyleId = sheet.GetCell(destination)!.StyleId;

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(source, source),
            [(source, sourceCell.Clone())],
            destination,
            PasteCellsMode.Values,
            default);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();

        var destinationCell = sheet.GetCell(destination)!;
        destinationCell.Value.Should().Be(new NumberValue(42));
        // Values-only paste must not touch formatting: destination keeps its own (unlocked) style.
        destinationCell.StyleId.Should().Be(originalDestinationStyleId);
    }
}
