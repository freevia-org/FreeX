using FreeX.App.Presentation.TextToColumns;
using FreeX.Core.Model;

using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Unit tests for the UI-free <see cref="TextToColumnsDialogPlanner"/>: turning dialog state into portable
/// <see cref="TextToColumnsOptions"/>, mapping a planned <see cref="TextToColumnsResult"/> over the source
/// column into cell edits (delimited and fixed-width, honoring Skip columns), and reporting the non-empty
/// cells an apply would overwrite. No running shell required.
/// </summary>
public sealed class TextToColumnsDialogPlannerTests
{
    [Fact]
    public void BuildOptions_Delimited_SelectsCheckedDelimitersAndQualifier()
    {
        var state = DelimitedState(comma: true, space: true, qualifier: TextToColumnsTextQualifier.SingleQuote);

        var options = TextToColumnsDialogPlanner.BuildOptions(state);

        options.SplitMode.Should().Be(TextToColumnsSplitMode.Delimited);
        options.Delimiters.Should().Contain(",").And.Contain(" ");
        options.TextQualifier.Should().Be('\'');
    }

    [Fact]
    public void BuildOptions_Delimited_NoDelimiterSelected_Throws()
    {
        var state = DelimitedState();

        var act = () => TextToColumnsDialogPlanner.BuildOptions(state);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void BuildOptions_FixedWidth_NormalizesBreakPositions()
    {
        var state = FixedWidthState(5, 5, 2, -1);

        var options = TextToColumnsDialogPlanner.BuildOptions(state);

        options.SplitMode.Should().Be(TextToColumnsSplitMode.FixedWidth);
        // Sorted, de-duplicated, non-positive dropped.
        options.FixedWidthBreakPositions.Should().Equal(2, 5);
    }

    [Fact]
    public void BuildOptions_FixedWidth_NoPositions_Throws()
    {
        var state = FixedWidthState();

        var act = () => TextToColumnsDialogPlanner.BuildOptions(state);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MapToEdits_Delimited_WritesFieldsAcrossColumnsFromSource()
    {
        var (_, sheet) = BuildWorkbook();
        // Source column B (col 2), rows 1..2.
        var range = Range(sheet.Id, 1, 2, 2, 2);
        var options = TextToColumnsOptions.Delimited([TextToColumnsDelimiterKind.Comma]);
        var result = TextToColumnsPlanner.Plan(["a,b,c", "x,y"], options);

        var edits = TextToColumnsDialogPlanner.MapToEdits(sheet.Id, result, range);

        // Row 1 -> B1,C1,D1; row 2 -> B2,C2 (D2 is left untouched, matching Excel).
        edits.Should().Contain(e => e.Address == new CellAddress(sheet.Id, 1, 2) && Text(e.NewCell) == "a");
        edits.Should().Contain(e => e.Address == new CellAddress(sheet.Id, 1, 3) && Text(e.NewCell) == "b");
        edits.Should().Contain(e => e.Address == new CellAddress(sheet.Id, 1, 4) && Text(e.NewCell) == "c");
        edits.Should().Contain(e => e.Address == new CellAddress(sheet.Id, 2, 2) && Text(e.NewCell) == "x");
        edits.Should().Contain(e => e.Address == new CellAddress(sheet.Id, 2, 3) && Text(e.NewCell) == "y");
    }

    [Fact]
    public void MapToEdits_DropsSkipColumnsAndShiftsTargets()
    {
        var (_, sheet) = BuildWorkbook();
        var range = Range(sheet.Id, 1, 1, 1, 1);
        // Skip the middle field: column formats General, Skip, General.
        var formats = new[]
        {
            TextToColumnsColumnFormat.General,
            TextToColumnsColumnFormat.Skip,
            TextToColumnsColumnFormat.General,
        };
        var options = TextToColumnsOptions.Delimited([TextToColumnsDelimiterKind.Comma], columnFormats: formats);
        var result = TextToColumnsPlanner.Plan(["a,b,c"], options);

        var edits = TextToColumnsDialogPlanner.MapToEdits(sheet.Id, result, range);

        // "b" is skipped; "a" -> A1, "c" -> B1 (the skip neither writes nor consumes a target column).
        edits.Should().HaveCount(2);
        edits.Should().Contain(e => e.Address == new CellAddress(sheet.Id, 1, 1) && Text(e.NewCell) == "a");
        edits.Should().Contain(e => e.Address == new CellAddress(sheet.Id, 1, 2) && Text(e.NewCell) == "c");
        edits.Should().NotContain(e => Text(e.NewCell) == "b");
    }

    [Fact]
    public void MapToEdits_FixedWidth_SlicesAtBreakPositions()
    {
        var (_, sheet) = BuildWorkbook();
        var range = Range(sheet.Id, 3, 1, 3, 1);
        var options = TextToColumnsOptions.FixedWidth([3]);
        var result = TextToColumnsPlanner.Plan(["abcdef"], options);

        var edits = TextToColumnsDialogPlanner.MapToEdits(sheet.Id, result, range);

        edits.Should().Contain(e => e.Address == new CellAddress(sheet.Id, 3, 1) && Text(e.NewCell) == "abc");
        edits.Should().Contain(e => e.Address == new CellAddress(sheet.Id, 3, 2) && Text(e.NewCell) == "def");
    }

    [Fact]
    public void FindOverwriteTargets_OnlyCountsNonEmptyCellsRightOfSource()
    {
        var (_, sheet) = BuildWorkbook();
        // Pre-existing content in C1 (to the right of source column B); B1 is the source column itself.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("existing"));
        var range = Range(sheet.Id, 1, 2, 1, 2);
        var options = TextToColumnsOptions.Delimited([TextToColumnsDelimiterKind.Comma]);
        var result = TextToColumnsPlanner.Plan(["a,b"], options);
        var edits = TextToColumnsDialogPlanner.MapToEdits(sheet.Id, result, range);

        var overwrites = TextToColumnsDialogPlanner.FindOverwriteTargets(sheet, edits, range);

        // Only C1 (col 3) is a non-empty overwrite; the source column B is excluded.
        overwrites.Should().ContainSingle()
            .Which.Should().Be(new CellAddress(sheet.Id, 1, 3));
    }

    private static TextToColumnsDialogState DelimitedState(
        bool tab = false,
        bool semicolon = false,
        bool comma = false,
        bool space = false,
        bool other = false,
        string? otherDelimiter = null,
        TextToColumnsTextQualifier qualifier = TextToColumnsTextQualifier.DoubleQuote) =>
        new(
            SplitMode: TextToColumnsSplitMode.Delimited,
            Tab: tab,
            Semicolon: semicolon,
            Comma: comma,
            Space: space,
            Other: other,
            OtherDelimiter: otherDelimiter,
            TreatConsecutiveDelimitersAsOne: false,
            TextQualifier: qualifier,
            FixedWidthBreakPositions: [],
            ColumnFormats: []);

    private static TextToColumnsDialogState FixedWidthState(params int[] breaks) =>
        new(
            SplitMode: TextToColumnsSplitMode.FixedWidth,
            Tab: false,
            Semicolon: false,
            Comma: false,
            Space: false,
            Other: false,
            OtherDelimiter: null,
            TreatConsecutiveDelimitersAsOne: false,
            TextQualifier: TextToColumnsTextQualifier.DoubleQuote,
            FixedWidthBreakPositions: breaks,
            ColumnFormats: []);

    private static (Workbook Workbook, Sheet Sheet) BuildWorkbook()
    {
        var workbook = new Workbook("Ttc");
        var sheet = workbook.AddSheet("Data");
        return (workbook, sheet);
    }

    private static string? Text(Cell cell) => (cell.Value as TextValue)?.Value;

    private static GridRange Range(SheetId sheetId, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(new CellAddress(sheetId, startRow, startCol), new CellAddress(sheetId, endRow, endCol));
}
