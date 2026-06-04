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
    public void CreateCellDiff_DrawUsesCurrentLineStyleAndColorOnlyOnRangeOutsideEdges()
    {
        var sheetId = SheetId.New();
        var range = Range(sheetId, 2, 3, 4, 5);

        var topLeft = BorderDrawPlanner.CreateCellDiff(
            BorderDrawMode.Draw,
            range,
            new CellAddress(sheetId, 2, 3),
            BorderStyle.Double,
            Accent);
        var center = BorderDrawPlanner.CreateCellDiff(
            BorderDrawMode.Draw,
            range,
            new CellAddress(sheetId, 3, 4),
            BorderStyle.Double,
            Accent);
        var bottomRight = BorderDrawPlanner.CreateCellDiff(
            BorderDrawMode.Draw,
            range,
            new CellAddress(sheetId, 4, 5),
            BorderStyle.Double,
            Accent);

        var expected = new CellBorder(BorderStyle.Double, Accent);
        topLeft.BorderTop.Should().Be(expected);
        topLeft.BorderLeft.Should().Be(expected);
        topLeft.BorderRight.Should().BeNull();
        topLeft.BorderBottom.Should().BeNull();

        center.BorderTop.Should().BeNull();
        center.BorderRight.Should().BeNull();
        center.BorderBottom.Should().BeNull();
        center.BorderLeft.Should().BeNull();

        bottomRight.BorderTop.Should().BeNull();
        bottomRight.BorderLeft.Should().BeNull();
        bottomRight.BorderRight.Should().Be(expected);
        bottomRight.BorderBottom.Should().Be(expected);
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
    public void CreateCommand_DrawAppliesRememberedBorderOnlyToDraggedRangeOutsideEdges()
    {
        var (workbook, sheet, context) = Setup();
        var range = Range(sheet.Id, 2, 3, 4, 5);
        var original = new CellBorder(BorderStyle.Dotted, new CellColor(9, 8, 7));
        var originalStyleId = workbook.RegisterStyle(new CellStyle
        {
            BorderTop = original,
            BorderRight = original,
            BorderBottom = original,
            BorderLeft = original
        });

        foreach (var address in range.AllCells())
        {
            sheet.SetCell(address, new NumberValue(1));
            sheet.GetCell(address)!.StyleId = originalStyleId;
        }

        var command = BorderDrawPlanner.CreateCommand(
            sheet.Id,
            range,
            BorderDrawMode.Draw,
            BorderStyle.Dashed,
            Accent);

        command.Label.Should().Be("Draw Border");
        command.Should().BeAssignableTo<IEstimatesMemory>()
            .Which.EstimatedBytes.Should().Be(1600);

        command.Apply(context).Success.Should().BeTrue();

        var expected = new CellBorder(BorderStyle.Dashed, Accent);
        AssertBorders(GetStyle(workbook, sheet, new CellAddress(sheet.Id, 2, 3)), expected, original, original, expected);
        AssertBorders(GetStyle(workbook, sheet, new CellAddress(sheet.Id, 2, 4)), expected, original, original, original);
        AssertBorders(GetStyle(workbook, sheet, new CellAddress(sheet.Id, 3, 4)), original, original, original, original);
        AssertBorders(GetStyle(workbook, sheet, new CellAddress(sheet.Id, 4, 5)), original, expected, expected, original);

        command.Revert(context);

        foreach (var address in range.AllCells())
            sheet.GetCell(address)!.StyleId.Should().Be(originalStyleId);
    }

    [Fact]
    public void CreateCommand_DrawSingleCellAppliesOutsideBorderToAllEdges()
    {
        var (workbook, sheet, context) = Setup();
        var range = Range(sheet.Id, 3, 4, 3, 4);

        var command = BorderDrawPlanner.CreateCommand(
            sheet.Id,
            range,
            BorderDrawMode.Draw,
            BorderStyle.Double,
            Accent);

        command.Apply(context).Success.Should().BeTrue();

        var expected = new CellBorder(BorderStyle.Double, Accent);
        AssertBorders(GetStyle(workbook, sheet, range.Start), expected, expected, expected, expected);
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
        return (workbook, sheet, new TestCommandContext(workbook));
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

    private static void AssertBorders(
        CellStyle style,
        CellBorder top,
        CellBorder right,
        CellBorder bottom,
        CellBorder left)
    {
        style.BorderTop.Should().Be(top);
        style.BorderRight.Should().Be(right);
        style.BorderBottom.Should().Be(bottom);
        style.BorderLeft.Should().Be(left);
    }

}
