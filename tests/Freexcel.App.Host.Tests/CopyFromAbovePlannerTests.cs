using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class CopyFromAbovePlannerTests
{
    [Fact]
    public void CreateEdit_ForFormulaModeCopiesFormulaTextFromCellAbove()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromFormula("A1*2"));

        var edit = CopyFromAbovePlanner.CreateEdit(
            sheet,
            new CellAddress(sheet.Id, 2, 2),
            CopyFromAboveMode.FormulaOrContent);

        edit.Should().NotBeNull();
        edit!.Value.NewCell.FormulaText.Should().Be("A1*2");
    }

    [Fact]
    public void CreateEdit_ForValueModeCopiesCalculatedValueFromCellAbove()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new Cell
        {
            FormulaText = "A1*2",
            Value = new NumberValue(10)
        });

        var edit = CopyFromAbovePlanner.CreateEdit(
            sheet,
            new CellAddress(sheet.Id, 2, 2),
            CopyFromAboveMode.Value);

        edit.Should().NotBeNull();
        edit!.Value.NewCell.FormulaText.Should().BeNull();
        edit.Value.NewCell.Value.Should().Be(new NumberValue(10));
    }

    [Fact]
    public void CreateEdit_DoesNothingOnFirstRow()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");

        CopyFromAbovePlanner.CreateEdit(
                sheet,
                new CellAddress(sheet.Id, 1, 2),
                CopyFromAboveMode.Value)
            .Should()
            .BeNull();
    }
}
