using FluentAssertions;
using FreeX.App.Presentation.QuickAnalysis;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.QuickAnalysis;

public sealed class QuickAnalysisSelectionInterpreterTests
{
    [Fact]
    public void Interpret_RejectsWholeColumnBeforeReadingCells()
    {
        var sheet = CreateSheet();
        var selection = Range(sheet, 1, 2, CellAddress.MaxRow, 2);

        var interpretation = QuickAnalysisSelectionInterpreter.Interpret(sheet, selection);

        interpretation.Eligibility.Should().Be(QuickAnalysisSelectionEligibility.WholeColumns);
        interpretation.Description.Should().BeNull();
    }

    [Fact]
    public void ClassifyEligibility_RejectsSelectionBeyondInteractiveInspectionLimit()
    {
        var sheet = CreateSheet();
        var rows = (uint)(QuickAnalysisSelectionInterpreter.MaximumAnalyzedCellCount / 2 + 1);
        var selection = Range(sheet, 1, 1, rows, 2);

        QuickAnalysisSelectionInterpreter.ClassifyEligibility(selection)
            .Should().Be(QuickAnalysisSelectionEligibility.TooLarge);
    }

    [Fact]
    public void Interpret_DescribesOrdinarySelection()
    {
        var sheet = CreateSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));

        var interpretation = QuickAnalysisSelectionInterpreter.Interpret(
            sheet,
            Range(sheet, 1, 1, 2, 1));

        interpretation.IsEligible.Should().BeTrue();
        interpretation.Description!.ColumnKinds.Should().Equal(QuickAnalysisColumnKind.Numeric);
    }

    private static Sheet CreateSheet() => new Workbook("Book").AddSheet("Sheet1");

    private static GridRange Range(Sheet sheet, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(new CellAddress(sheet.Id, startRow, startCol), new CellAddress(sheet.Id, endRow, endCol));
}
