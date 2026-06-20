using FluentAssertions;
using FreeX.App.Presentation.QuickAnalysis;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.QuickAnalysis;

public sealed class QuickAnalysisSelectionReaderTests
{
    private static Sheet CreateSheet() => new Workbook("Book").AddSheet("Sheet1");

    private static GridRange Range(Sheet sheet, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(new CellAddress(sheet.Id, startRow, startCol), new CellAddress(sheet.Id, endRow, endCol));

    [Fact]
    public void Describe_ClassifiesColumnsByContent()
    {
        var sheet = CreateSheet();
        for (uint row = 1; row <= 3; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue($"name{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 3), DateTimeValue.FromDateTime(new DateTime(2026, 1, (int)row)));
        }

        var description = QuickAnalysisSelectionReader.Describe(sheet, Range(sheet, 1, 1, 3, 4));

        description.ColumnKinds.Should().Equal(
            QuickAnalysisColumnKind.Numeric,
            QuickAnalysisColumnKind.Text,
            QuickAnalysisColumnKind.Date,
            QuickAnalysisColumnKind.Empty);
    }

    [Fact]
    public void Describe_DetectsHeaderRow_WhenFirstRowAllTextOverNumericData()
    {
        var sheet = CreateSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        for (uint row = 2; row <= 4; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"R{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row * 10));
        }

        var description = QuickAnalysisSelectionReader.Describe(sheet, Range(sheet, 1, 1, 4, 2));

        description.HasHeaderRow.Should().BeTrue();
        description.ColumnKinds.Should().Equal(QuickAnalysisColumnKind.Text, QuickAnalysisColumnKind.Numeric);
        description.DataRowCount.Should().Be(3u);
    }

    [Fact]
    public void Describe_NoHeaderRow_WhenAllColumnsAreText()
    {
        var sheet = CreateSheet();
        for (uint row = 1; row <= 3; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"a{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue($"b{row}"));
        }

        var description = QuickAnalysisSelectionReader.Describe(sheet, Range(sheet, 1, 1, 3, 2));

        description.HasHeaderRow.Should().BeFalse();
    }
}
