using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class BorderDrawPlannerTests
{
    private static readonly CellColor Accent = new(33, 115, 70);

    [Fact]
    public void CreateDiff_DrawGridUsesCurrentLineStyleAndColor()
    {
        var diff = BorderDrawPlanner.CreateDiff(BorderDrawMode.DrawGrid, BorderStyle.Double, Accent);

        var expected = new CellBorder(BorderStyle.Double, Accent);
        diff.BorderTop.Should().Be(expected);
        diff.BorderRight.Should().Be(expected);
        diff.BorderBottom.Should().Be(expected);
        diff.BorderLeft.Should().Be(expected);
    }

    [Fact]
    public void CreateDiff_EraseClearsAllBorders()
    {
        var diff = BorderDrawPlanner.CreateDiff(BorderDrawMode.Erase, BorderStyle.Thick, new CellColor(1, 2, 3));

        diff.BorderTop.Should().Be(new CellBorder(BorderStyle.None));
        diff.BorderRight.Should().Be(new CellBorder(BorderStyle.None));
        diff.BorderBottom.Should().Be(new CellBorder(BorderStyle.None));
        diff.BorderLeft.Should().Be(new CellBorder(BorderStyle.None));
    }

    [Fact]
    public void CreateCommand_DrawGridAppliesRememberedBorderToDraggedRange()
    {
        var (workbook, sheet, context) = Setup();
        var range = Range(sheet.Id, 2, 3, 3, 4);

        var command = BorderDrawPlanner.CreateCommand(
            sheet.Id,
            range,
            BorderDrawMode.DrawGrid,
            BorderStyle.Dashed,
            Accent);

        command.Label.Should().Be("Draw Border Grid");
        command.Should().BeAssignableTo<IEstimatesMemory>()
            .Which.EstimatedBytes.Should().Be(800);

        command.Apply(context).Success.Should().BeTrue();

        var expected = new CellBorder(BorderStyle.Dashed, Accent);
        foreach (var address in range.AllCells())
        {
            var style = GetStyle(workbook, sheet, address);
            style.BorderTop.Should().Be(expected);
            style.BorderRight.Should().Be(expected);
            style.BorderBottom.Should().Be(expected);
            style.BorderLeft.Should().Be(expected);
        }

        command.Revert(context);

        foreach (var address in range.AllCells())
            sheet.GetStyleOnly(address.Row, address.Col).Should().BeNull();
    }

    [Fact]
    public void CreateCommand_EraseClearsBordersAcrossDraggedRange()
    {
        var (workbook, sheet, context) = Setup();
        var range = Range(sheet.Id, 2, 3, 3, 4);
        var borderedStyleId = workbook.RegisterStyle(new CellStyle
        {
            BorderTop = new CellBorder(BorderStyle.Thick, Accent),
            BorderRight = new CellBorder(BorderStyle.Thick, Accent),
            BorderBottom = new CellBorder(BorderStyle.Thick, Accent),
            BorderLeft = new CellBorder(BorderStyle.Thick, Accent)
        });

        foreach (var address in range.AllCells())
        {
            sheet.SetCell(address, new NumberValue(1));
            sheet.GetCell(address)!.StyleId = borderedStyleId;
        }

        var command = BorderDrawPlanner.CreateCommand(
            sheet.Id,
            range,
            BorderDrawMode.Erase,
            BorderStyle.Double,
            new CellColor(1, 2, 3));

        command.Label.Should().Be("Erase Border");

        command.Apply(context).Success.Should().BeTrue();

        foreach (var address in range.AllCells())
        {
            var style = GetStyle(workbook, sheet, address);
            style.BorderTop.Should().Be(new CellBorder(BorderStyle.None));
            style.BorderRight.Should().Be(new CellBorder(BorderStyle.None));
            style.BorderBottom.Should().Be(new CellBorder(BorderStyle.None));
            style.BorderLeft.Should().Be(new CellBorder(BorderStyle.None));
        }

        command.Revert(context);

        foreach (var address in range.AllCells())
            sheet.GetCell(address)!.StyleId.Should().Be(borderedStyleId);
    }

    [Fact]
    public void CreateCommand_RejectsInactiveMode()
    {
        var sheetId = SheetId.New();
        var range = Range(sheetId, 1, 1, 1, 1);

        var act = () => BorderDrawPlanner.CreateCommand(
            sheetId,
            range,
            BorderDrawMode.None,
            BorderStyle.Thin,
            CellColor.Black);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("mode");
    }

    private static (Workbook Workbook, Sheet Sheet, ICommandContext Context) Setup()
    {
        var workbook = new Workbook("BorderDrawPlannerTests");
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet, new SimpleContext(workbook));
    }

    private static GridRange Range(SheetId sheetId, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(
            new CellAddress(sheetId, startRow, startCol),
            new CellAddress(sheetId, endRow, endCol));

    private static CellStyle GetStyle(Workbook workbook, Sheet sheet, CellAddress address)
    {
        var styleId = sheet.GetCell(address)?.StyleId ??
            sheet.GetStyleOnly(address.Row, address.Col) ??
            StyleId.Default;
        return workbook.GetStyle(styleId);
    }

    private sealed class SimpleContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) => Workbook.GetSheet(sheetId)!;
    }
}
