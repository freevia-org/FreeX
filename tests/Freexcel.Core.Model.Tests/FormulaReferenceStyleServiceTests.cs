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
    public void ToR1C1_PreservesExternalWorkbookQualifiersWhenConvertingA1References()
    {
        var sheetId = SheetId.New();
        var anchor = new CellAddress(sheetId, 3, 3);

        var result = FormulaReferenceStyleService.ToR1C1("[Book.xlsx]Sheet2!B1+'[Budget 2026.xlsx]My Sheet'!$A$1", anchor);

        result.Should().Be("[Book.xlsx]Sheet2!R[-2]C[-1]+'[Budget 2026.xlsx]My Sheet'!R1C1");
    }

    [Fact]
    public void ToA1_PreservesExternalWorkbookQualifiersWhenConvertingR1C1References()
    {
        var sheetId = SheetId.New();
        var anchor = new CellAddress(sheetId, 3, 3);

        var result = FormulaReferenceStyleService.ToA1("[Book.xlsx]Sheet2!R[-2]C[-1]+'[Budget 2026.xlsx]My Sheet'!R1C1", anchor);

        result.Should().Be("[Book.xlsx]Sheet2!B1+'[Budget 2026.xlsx]My Sheet'!$A$1");
    }

    [Fact]
    public void ToR1C1_DoesNotConvertA1LikeTextInsideExternalWorkbookNames()
    {
        var sheetId = SheetId.New();
        var anchor = new CellAddress(sheetId, 3, 3);

        var result = FormulaReferenceStyleService.ToR1C1("[A1.xlsx]Sheet2!B2+'[Q4 A1.xlsx]My Sheet'!C3", anchor);

        result.Should().Be("[A1.xlsx]Sheet2!R[-1]C[-1]+'[Q4 A1.xlsx]My Sheet'!RC");
    }

    [Fact]
    public void ToR1C1_DoesNotConvertSheetNamesInsideThreeDimensionalReferences()
    {
        var sheetId = SheetId.New();
        var anchor = new CellAddress(sheetId, 3, 3);

        var result = FormulaReferenceStyleService.ToR1C1("SUM(Sheet1:Sheet3!A1,Sheet2!B2)", anchor);

        result.Should().Be("SUM(Sheet1:Sheet3!R[-2]C[-2],Sheet2!R[-1]C[-1])");
    }

    [Fact]
    public void ToA1_PreservesThreeDimensionalReferenceQualifiers()
    {
        var sheetId = SheetId.New();
        var anchor = new CellAddress(sheetId, 3, 3);

        var result = FormulaReferenceStyleService.ToA1("SUM(Sheet1:Sheet3!R[-2]C[-2],Sheet2!R[-1]C[-1])", anchor);

        result.Should().Be("SUM(Sheet1:Sheet3!A1,Sheet2!B2)");
    }

    [Fact]
    public void ToR1C1_DoesNotConvertA1LikeTextInsideQuotedThreeDimensionalSheetQualifiers()
    {
        var sheetId = SheetId.New();
        var anchor = new CellAddress(sheetId, 3, 3);

        var result = FormulaReferenceStyleService.ToR1C1("'A1:B2'!C3+'Q4 A1:Q4 B2'!D4", anchor);

        result.Should().Be("'A1:B2'!RC+'Q4 A1:Q4 B2'!R[1]C[1]");
    }

    [Fact]
    public void ToA1_DoesNotConvertR1C1LikeTextInsideQuotedThreeDimensionalSheetQualifiers()
    {
        var sheetId = SheetId.New();
        var anchor = new CellAddress(sheetId, 3, 3);

        var result = FormulaReferenceStyleService.ToA1("'R1C1:R2C2'!RC+'Q4 R1C1:Q4 R2C2'!R[1]C[1]", anchor);

        result.Should().Be("'R1C1:R2C2'!C3+'Q4 R1C1:Q4 R2C2'!D4");
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

    [Fact]
    public void ToA1_DoesNotConvertStructuredReferenceColumnNamesThatLookLikeR1C1References()
    {
        var sheetId = SheetId.New();
        var anchor = new CellAddress(sheetId, 3, 3);

        var result = FormulaReferenceStyleService.ToA1("SUM(Table1[R1C1],R[-2]C[-2])", anchor);

        result.Should().Be("SUM(Table1[R1C1],A1)");
    }

    [Fact]
    public void ToR1C1_LeavesOverflowingA1RowsUnchanged()
    {
        var sheetId = SheetId.New();
        var anchor = new CellAddress(sheetId, 3, 3);

        var result = FormulaReferenceStyleService.ToR1C1("A999999999999999999999", anchor);

        result.Should().Be("A999999999999999999999");
    }

    [Fact]
    public void ToA1_LeavesOverflowingR1C1PartsUnchanged()
    {
        var sheetId = SheetId.New();
        var anchor = new CellAddress(sheetId, 3, 3);

        var result = FormulaReferenceStyleService.ToA1("R999999999999999999999C1+R[999999999999999999999]C", anchor);

        result.Should().Be("R999999999999999999999C1+R[999999999999999999999]C");
    }
}
