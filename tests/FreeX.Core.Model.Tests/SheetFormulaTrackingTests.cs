using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class SheetFormulaTrackingTests
{
    [Fact]
    public void FormulaCellCount_TracksSetFormulaSetCellAndClearCell()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);

        sheet.HasFormulas.Should().BeFalse();
        sheet.FormulaCellCount.Should().Be(0);

        sheet.SetFormula(a1, "1+1");
        sheet.SetCell(b1, Cell.FromFormula("A1*2"));

        sheet.HasFormulas.Should().BeTrue();
        sheet.FormulaCellCount.Should().Be(2);

        sheet.SetCell(a1, new NumberValue(2));

        sheet.HasFormulas.Should().BeTrue();
        sheet.FormulaCellCount.Should().Be(1);

        sheet.SetCell(b1, Cell.FromValue(new NumberValue(4)));

        sheet.HasFormulas.Should().BeFalse();
        sheet.FormulaCellCount.Should().Be(0);
    }

    [Fact]
    public void FormulaCellCount_TracksReplacementAndRemovalWithoutDoubleCounting()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var a1 = new CellAddress(sheet.Id, 1, 1);

        sheet.SetFormula(a1, "1+1");
        sheet.SetFormula(a1, "2+2");
        sheet.SetCell(a1, Cell.FromFormula("3+3"));

        sheet.FormulaCellCount.Should().Be(1);

        sheet.ClearCell(a1);

        sheet.HasFormulas.Should().BeFalse();
        sheet.FormulaCellCount.Should().Be(0);
    }
}
