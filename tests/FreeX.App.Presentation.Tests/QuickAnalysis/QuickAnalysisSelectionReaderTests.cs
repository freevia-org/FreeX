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

    [Fact]
    public void Describe_DetectsHeaderRow_WhenOneColumnHeadingIsNumeric()
    {
        // R61-commands-sort-multilevel-6-2: A1=2023 (a numeric year heading), B1="Revenue" (text).
        // Real Excel still recognizes row 1 as a header because column B shows the classic
        // "label over values" shape, even though column A's heading happens to be a number matching
        // its own column's data type.
        var sheet = CreateSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(2023));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Revenue"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2024));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(500));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(2025));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(300));

        var description = QuickAnalysisSelectionReader.Describe(sheet, Range(sheet, 1, 1, 3, 2));

        description.HasHeaderRow.Should().BeTrue();
        description.DataRowCount.Should().Be(2u);
    }

    [Fact]
    public void Describe_NoHeaderRow_WhenNumericHeadingSitsOverPureTextColumn()
    {
        // Sibling no-regression case: a numeric "header" cell sitting over a column that is purely
        // text below (no other column shows a label-over-values shape either) should NOT be treated
        // as a header -- it looks like a stray data value sitting over label data, not a heading.
        var sheet = CreateSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Q1"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("apple"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Q2"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("banana"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("Q3"));

        var description = QuickAnalysisSelectionReader.Describe(sheet, Range(sheet, 1, 1, 3, 2));

        description.HasHeaderRow.Should().BeFalse();
    }

    [Fact]
    public void Describe_FullStructuredTableUsesExplicitHeaderAndExcludesTotalsFromData()
    {
        var sheet = CreateSheet();
        var tableRange = Range(sheet, 1, 1, 5, 2);
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 7,
            Name = "Sales",
            DisplayName = "Sales",
            Range = tableRange,
            HeaderRowCount = 1,
            TotalsRowShown = true,
            TotalsRowCount = 1
        });
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        for (uint row = 2; row <= 4; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"R{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row * 10));
        }
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new NumberValue(999));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(999));

        var description = QuickAnalysisSelectionReader.Describe(sheet, tableRange);

        description.IsStructuredTableSelection.Should().BeTrue();
        description.HasHeaderRow.Should().BeTrue();
        description.DataRowCount.Should().Be(3);
        description.ColumnKinds.Should().Equal(
            QuickAnalysisColumnKind.Text,
            QuickAnalysisColumnKind.Numeric);
    }
}
