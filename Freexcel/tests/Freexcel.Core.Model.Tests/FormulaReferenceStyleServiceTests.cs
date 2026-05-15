using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.Core.Model.Tests;

public sealed class FormulaReferenceStyleServiceTests
{
    [Fact]
    public void ToR1C1_ConvertsRelativeAbsoluteAndMixedA1References()
    {
        var sheetId = SheetId.New();
        var anchor = new CellAddress(sheetId, 3, 3);

        var result = FormulaReferenceStyleService.ToR1C1("B1+$A$1+$A1+A$1", anchor);

        result.Should().Be("R[-2]C[-1]+R1C1+R[-2]C1+R1C[-2]");
    }

    [Fact]
    public void ToA1_ConvertsRelativeAbsoluteAndMixedR1C1References()
    {
        var sheetId = SheetId.New();
        var anchor = new CellAddress(sheetId, 3, 3);

        var result = FormulaReferenceStyleService.ToA1("R[-2]C[-1]+R1C1+R[-2]C1+R1C[-2]", anchor);

        result.Should().Be("B1+$A$1+$A1+A$1");
    }

    [Fact]
    public void ToR1C1_PreservesSheetQualifiersWhenConvertingA1References()
    {
        var sheetId = SheetId.New();
        var anchor = new CellAddress(sheetId, 3, 3);

        var result = FormulaReferenceStyleService.ToR1C1("Sheet2!B1+'My Sheet'!$A$1", anchor);

        result.Should().Be("Sheet2!R[-2]C[-1]+'My Sheet'!R1C1");
    }

    [Fact]
    public void ToA1_PreservesSheetQualifiersWhenConvertingR1C1References()
    {
        var sheetId = SheetId.New();
        var anchor = new CellAddress(sheetId, 3, 3);

        var result = FormulaReferenceStyleService.ToA1("Sheet2!R[-2]C[-1]+'My Sheet'!R1C1", anchor);

        result.Should().Be("Sheet2!B1+'My Sheet'!$A$1");
    }

    [Fact]
    public void ToR1C1_LeavesA1LikeReferencesOutsideExcelGridUnchanged()
    {
        var sheetId = SheetId.New();
        var anchor = new CellAddress(sheetId, 3, 3);

        var result = FormulaReferenceStyleService.ToR1C1("XFE1+ZZZ1+A1048577", anchor);

        result.Should().Be("XFE1+ZZZ1+A1048577");
    }

    [Fact]
    public void ToR1C1_DoesNotConvertA1TextInsideStringLiterals()
    {
        var sheetId = SheetId.New();
        var anchor = new CellAddress(sheetId, 3, 3);

        var result = FormulaReferenceStyleService.ToR1C1("A1 & \"B2\" & C3 & \"said \"\"D4\"\"\"", anchor);

        result.Should().Be("R[-2]C[-2] & \"B2\" & RC & \"said \"\"D4\"\"\"");
    }

    [Fact]
    public void ToA1_DoesNotConvertR1C1TextInsideStringLiterals()
    {
        var sheetId = SheetId.New();
        var anchor = new CellAddress(sheetId, 3, 3);

        var result = FormulaReferenceStyleService.ToA1("R[-2]C[-2] & \"R1C1\" & R[0]C[0] & \"said \"\"R4C4\"\"\"", anchor);

        result.Should().Be("A1 & \"R1C1\" & C3 & \"said \"\"R4C4\"\"\"");
    }

    [Fact]
    public void ToR1C1_DoesNotConvertStructuredReferenceColumnNamesThatLookLikeA1References()
    {
        var sheetId = SheetId.New();
        var anchor = new CellAddress(sheetId, 3, 3);

        var result = FormulaReferenceStyleService.ToR1C1("SUM(Table1[A1],A1)", anchor);

        result.Should().Be("SUM(Table1[A1],R[-2]C[-2])");
    }
}
