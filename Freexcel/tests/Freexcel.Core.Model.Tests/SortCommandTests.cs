using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.Core.Model.Tests;

public sealed class SortCommandTests
{
    [Fact]
    public void SortCommand_SupportsMultipleSortKeysAndUndoRestoresRows()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new SimpleCtx(workbook);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(15));
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2));

        var command = new SortCommand(sheet.Id, range, [new SortKey(0, true), new SortKey(1, false)]);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(1, 1).Should().Be(new TextValue("East"));
        sheet.GetValue(1, 2).Should().Be(new NumberValue(15));
        sheet.GetValue(2, 1).Should().Be(new TextValue("East"));
        sheet.GetValue(2, 2).Should().Be(new NumberValue(10));
        sheet.GetValue(3, 1).Should().Be(new TextValue("West"));
        sheet.GetValue(3, 2).Should().Be(new NumberValue(20));
        sheet.GetValue(4, 1).Should().Be(new TextValue("West"));
        sheet.GetValue(4, 2).Should().Be(new NumberValue(5));

        command.Revert(ctx);

        sheet.GetValue(1, 1).Should().Be(new TextValue("West"));
        sheet.GetValue(1, 2).Should().Be(new NumberValue(5));
        sheet.GetValue(4, 1).Should().Be(new TextValue("East"));
        sheet.GetValue(4, 2).Should().Be(new NumberValue(15));
    }

    [Fact]
    public void SortCommand_SupportsCaseSensitiveTextOrder()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new SimpleCtx(workbook);
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("apple"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Banana"));

        var command = new SortCommand(sheet.Id, range, [new SortKey(0, true)], new SortOptions(CaseSensitive: true));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(1, 1).Should().Be(new TextValue("Banana"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("apple"));
    }

    [Fact]
    public void SortCommand_LeftToRight_SortsColumnsByRowKeyAndUndoRestores()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new SimpleCtx(workbook);
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 3));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(3));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("C"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("B"));

        var command = new SortCommand(sheet.Id, range, [new SortKey(0, true)], new SortOptions(LeftToRight: true));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(1, 1).Should().Be(new NumberValue(1));
        sheet.GetValue(2, 1).Should().Be(new TextValue("A"));
        sheet.GetValue(1, 2).Should().Be(new NumberValue(2));
        sheet.GetValue(2, 2).Should().Be(new TextValue("B"));
        sheet.GetValue(1, 3).Should().Be(new NumberValue(3));
        sheet.GetValue(2, 3).Should().Be(new TextValue("C"));

        command.Revert(ctx);

        sheet.GetValue(1, 1).Should().Be(new NumberValue(3));
        sheet.GetValue(2, 1).Should().Be(new TextValue("C"));
        sheet.GetValue(1, 2).Should().Be(new NumberValue(1));
        sheet.GetValue(2, 2).Should().Be(new TextValue("A"));
    }

    [Fact]
    public void SortCommand_CanSortRowsByCellFillColor()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new SimpleCtx(workbook);
        var redStyle = workbook.RegisterStyle(new CellStyle { FillColor = new CellColor(255, 0, 0) });
        var blueStyle = workbook.RegisterStyle(new CellStyle { FillColor = new CellColor(0, 0, 255) });
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2));
        SetStyledRow(sheet, 1, "Plain", StyleId.Default);
        SetStyledRow(sheet, 2, "Blue", blueStyle);
        SetStyledRow(sheet, 3, "Red", redStyle);

        var command = new SortCommand(sheet.Id, range, [new SortKey(0, true, SortOn.CellColor)]);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(1, 2).Should().Be(new TextValue("Blue"));
        sheet.GetValue(2, 2).Should().Be(new TextValue("Red"));
        sheet.GetValue(3, 2).Should().Be(new TextValue("Plain"));
    }

    [Fact]
    public void SortCommand_CellFillColorTarget_MovesSelectedColorToTop()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new SimpleCtx(workbook);
        var red = new CellColor(255, 0, 0);
        var blueStyle = workbook.RegisterStyle(new CellStyle { FillColor = new CellColor(0, 0, 255) });
        var redStyle = workbook.RegisterStyle(new CellStyle { FillColor = red });
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2));
        SetStyledRow(sheet, 1, "Plain", StyleId.Default);
        SetStyledRow(sheet, 2, "Blue", blueStyle);
        SetStyledRow(sheet, 3, "Red 1", redStyle);
        SetStyledRow(sheet, 4, "Red 2", redStyle);

        var command = new SortCommand(sheet.Id, range, [new SortKey(0, true, SortOn.CellColor, red)]);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(1, 2).Should().Be(new TextValue("Red 1"));
        sheet.GetValue(2, 2).Should().Be(new TextValue("Red 2"));
        sheet.GetValue(3, 2).Should().Be(new TextValue("Plain"));
        sheet.GetValue(4, 2).Should().Be(new TextValue("Blue"));
    }

    [Fact]
    public void SortCommand_CanSortRowsByFontColorDescending()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new SimpleCtx(workbook);
        var redStyle = workbook.RegisterStyle(new CellStyle { FontColor = new CellColor(255, 0, 0) });
        var blueStyle = workbook.RegisterStyle(new CellStyle { FontColor = new CellColor(0, 0, 255) });
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2));
        SetStyledRow(sheet, 1, "Plain", StyleId.Default);
        SetStyledRow(sheet, 2, "Blue", blueStyle);
        SetStyledRow(sheet, 3, "Red", redStyle);

        var command = new SortCommand(sheet.Id, range, [new SortKey(0, false, SortOn.FontColor)]);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(1, 2).Should().Be(new TextValue("Red"));
        sheet.GetValue(2, 2).Should().Be(new TextValue("Blue"));
        sheet.GetValue(3, 2).Should().Be(new TextValue("Plain"));
    }

    [Fact]
    public void SortCommand_FontColorTarget_MovesSelectedColorToBottom()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new SimpleCtx(workbook);
        var blue = new CellColor(0, 0, 255);
        var redStyle = workbook.RegisterStyle(new CellStyle { FontColor = new CellColor(255, 0, 0) });
        var blueStyle = workbook.RegisterStyle(new CellStyle { FontColor = blue });
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2));
        SetStyledRow(sheet, 1, "Plain", StyleId.Default);
        SetStyledRow(sheet, 2, "Blue 1", blueStyle);
        SetStyledRow(sheet, 3, "Red", redStyle);
        SetStyledRow(sheet, 4, "Blue 2", blueStyle);

        var command = new SortCommand(sheet.Id, range, [new SortKey(0, false, SortOn.FontColor, blue)]);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(1, 2).Should().Be(new TextValue("Plain"));
        sheet.GetValue(2, 2).Should().Be(new TextValue("Red"));
        sheet.GetValue(3, 2).Should().Be(new TextValue("Blue 1"));
        sheet.GetValue(4, 2).Should().Be(new TextValue("Blue 2"));
    }

    private static void SetStyledRow(Sheet sheet, uint row, string label, StyleId styleId)
    {
        var keyCell = Cell.FromValue(new TextValue(label));
        keyCell.StyleId = styleId;
        sheet.SetCell(new CellAddress(sheet.Id, row, 1), keyCell);
        sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(label));
    }

    private sealed class SimpleCtx(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;
        public Sheet GetSheet(SheetId sheetId) => Workbook.GetSheet(sheetId)!;
    }
}
