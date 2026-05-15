using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.Core.Model.Tests;

public sealed class GroupedApplyStyleCommandTests
{
    [Fact]
    public void Apply_AppliesStyleToSameRangeOnGroupedSheetsAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var ctx = new SimpleCtx(wb);
        var sourceRange = new GridRange(
            new CellAddress(sheet1.Id, 1, 1),
            new CellAddress(sheet1.Id, 1, 2));

        var command = new GroupedApplyStyleCommand(
            [sheet1.Id, sheet2.Id],
            sourceRange,
            new StyleDiff(Bold: true));

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        wb.GetStyle(sheet1.GetCell(new CellAddress(sheet1.Id, 1, 1))!.StyleId).Bold.Should().BeTrue();
        wb.GetStyle(sheet1.GetCell(new CellAddress(sheet1.Id, 1, 2))!.StyleId).Bold.Should().BeTrue();
        wb.GetStyle(sheet2.GetCell(new CellAddress(sheet2.Id, 1, 1))!.StyleId).Bold.Should().BeTrue();
        wb.GetStyle(sheet2.GetCell(new CellAddress(sheet2.Id, 1, 2))!.StyleId).Bold.Should().BeTrue();

        command.Revert(ctx);

        sheet1.GetCell(new CellAddress(sheet1.Id, 1, 1)).Should().BeNull();
        sheet1.GetCell(new CellAddress(sheet1.Id, 1, 2)).Should().BeNull();
        sheet2.GetCell(new CellAddress(sheet2.Id, 1, 1)).Should().BeNull();
        sheet2.GetCell(new CellAddress(sheet2.Id, 1, 2)).Should().BeNull();
    }

    [Fact]
    public void Apply_RejectsProtectedGroupedSheetBeforeChangingAnySheet()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var ctx = new SimpleCtx(wb);
        var address1 = new CellAddress(sheet1.Id, 1, 1);
        var address2 = new CellAddress(sheet2.Id, 1, 1);
        sheet1.SetCell(address1, Cell.FromValue(new TextValue("old1")));
        sheet2.SetCell(address2, Cell.FromValue(new TextValue("old2")));
        var oldStyle1 = sheet1.GetCell(address1)!.StyleId;
        var oldStyle2 = sheet2.GetCell(address2)!.StyleId;
        sheet2.IsProtected = true;

        var command = new GroupedApplyStyleCommand(
            [sheet1.Id, sheet2.Id],
            new GridRange(address1, address1),
            new StyleDiff(Italic: true));

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet1.GetCell(address1)!.StyleId.Should().Be(oldStyle1);
        sheet2.GetCell(address2)!.StyleId.Should().Be(oldStyle2);
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
