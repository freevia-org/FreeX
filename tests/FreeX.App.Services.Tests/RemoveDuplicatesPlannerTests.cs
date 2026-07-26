using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class RemoveDuplicatesPlannerTests
{
    [Fact]
    public void BuildColumnChoices_UsesHeaderLabelsAndColumnLetterFallbacks()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var range = Range(sheet, 1, 1, 4, 3);
        sheet.SetCell(Address(sheet, 1, 1), new TextValue("Region"));
        sheet.SetCell(Address(sheet, 1, 3), new TextValue("Amount"));

        var choices = RemoveDuplicatesPlanner.BuildColumnChoices(sheet, range, hasHeaders: true);

        choices.Should().Equal(
            new RemoveDuplicateColumnChoice(0, "Region", true),
            new RemoveDuplicateColumnChoice(1, "Column B", true),
            new RemoveDuplicateColumnChoice(2, "Amount", true));
    }

    [Fact]
    public void BuildColumnChoices_UsesColumnLettersWhenHeadersAreDisabled()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var range = Range(sheet, 2, 2, 6, 4);
        sheet.SetCell(Address(sheet, 2, 2), new TextValue("Region"));

        var choices = RemoveDuplicatesPlanner.BuildColumnChoices(sheet, range, hasHeaders: false);

        choices.Should().Equal(
            new RemoveDuplicateColumnChoice(0, "Column B", true),
            new RemoveDuplicateColumnChoice(1, "Column C", true),
            new RemoveDuplicateColumnChoice(2, "Column D", true));
    }

    [Fact]
    public void GuessHasHeaders_RecognizesTextHeadersOverTypedBodyValues()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var range = Range(sheet, 1, 1, 3, 2);
        sheet.SetCell(Address(sheet, 1, 1), new TextValue("Region"));
        sheet.SetCell(Address(sheet, 1, 2), new TextValue("Amount"));
        sheet.SetCell(Address(sheet, 2, 1), new TextValue("North"));
        sheet.SetCell(Address(sheet, 2, 2), new NumberValue(42));

        var hasHeaders = RemoveDuplicatesPlanner.GuessHasHeaders(sheet, range);

        hasHeaders.Should().BeTrue();
    }

    [Fact]
    public void GuessHasHeaders_RejectsSingleRowRanges()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var singleRow = Range(sheet, 1, 1, 1, 2);
        sheet.SetCell(Address(sheet, 1, 1), new TextValue("Region"));
        sheet.SetCell(Address(sheet, 1, 2), new TextValue("Rep"));

        RemoveDuplicatesPlanner.GuessHasHeaders(sheet, singleRow).Should().BeFalse();
    }

    // R90-commands-remove-duplicates-consolidate-5-2: this used to assert False for an all-text
    // body (every column text-typed, header labels distinct from the data beneath them), which
    // was the bug itself -- real Excel guesses "has headers" here (see
    // R90_RemoveDuplicatesGuessHasHeadersAllTextTests for the full duplicate-row scenario). The
    // label-vs-data-recurrence heuristic now correctly recognizes "Region"/"Rep" as header labels
    // because neither word reappears as a data value in its own column.
    [Fact]
    public void GuessHasHeaders_RecognizesAllTextHeaderRowWhenLabelsDoNotRecurAsData()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var allText = Range(sheet, 1, 1, 2, 2);
        sheet.SetCell(Address(sheet, 1, 1), new TextValue("Region"));
        sheet.SetCell(Address(sheet, 1, 2), new TextValue("Rep"));
        sheet.SetCell(Address(sheet, 2, 1), new TextValue("North"));
        sheet.SetCell(Address(sheet, 2, 2), new TextValue("Ada"));

        RemoveDuplicatesPlanner.GuessHasHeaders(sheet, allText).Should().BeTrue();
    }

    [Fact]
    public void CreatePlan_ExcludesHeaderRowAndCapturesSelectedColumnOffsets()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var sourceRange = Range(sheet, 1, 1, 5, 3);

        var result = RemoveDuplicatesPlanner.CreatePlan(
            sourceRange,
            hasHeaders: true,
            [
                new RemoveDuplicateColumnChoice(0, "Region", true),
                new RemoveDuplicateColumnChoice(1, "Rep", false),
                new RemoveDuplicateColumnChoice(2, "Amount", true),
            ]);

        result.IsReady.Should().BeTrue();
        result.Plan.Should().NotBeNull();
        result.Plan!.SourceRange.Should().Be(sourceRange);
        result.Plan.ActiveRange.Should().Be(Range(sheet, 2, 1, 5, 3));
        result.Plan.HasHeaders.Should().BeTrue();
        result.Plan.SelectedColumnOffsets.Should().Equal(0u, 2u);
        result.Plan.ActiveRangeForSheet(SheetId.New()).Start.Row.Should().Be(2);
        result.Plan.CreateCommand(sheet.Id, result.Plan.ActiveRange)
            .Should()
            .BeOfType<RemoveDuplicateRowsCommand>();
        result.Plan.CreateCommand(sheet.Id)
            .Should()
            .BeOfType<RemoveDuplicateRowsCommand>();
    }

    [Fact]
    public void CreatePlan_CanUseSelectedOffsetsFromHostDialog()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var sourceRange = Range(sheet, 1, 1, 5, 3);

        var result = RemoveDuplicatesPlanner.CreatePlan(
            sourceRange,
            hasHeaders: true,
            selectedColumnOffsets: [0u, 2u]);

        result.IsReady.Should().BeTrue();
        result.Plan.Should().NotBeNull();
        result.Plan!.ActiveRange.Should().Be(Range(sheet, 2, 1, 5, 3));
        result.Plan.SelectedColumnOffsets.Should().Equal(0u, 2u);
        result.Plan.ActiveRangeForSheet(SheetId.New()).Start.Row.Should().Be(2);
        result.Plan.CreateCommand(SheetId.New()).Should().BeOfType<RemoveDuplicateRowsCommand>();
    }

    [Fact]
    public void CreatePlan_RejectsRequestsWithoutSelectedColumns()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();

        var result = RemoveDuplicatesPlanner.CreatePlan(
            Range(sheet, 1, 1, 4, 2),
            hasHeaders: false,
            [
                new RemoveDuplicateColumnChoice(0, "Column A", false),
                new RemoveDuplicateColumnChoice(1, "Column B", false),
            ]);

        result.IsReady.Should().BeFalse();
        result.Status.Should().Be(RemoveDuplicatesPlanStatus.NoColumnsSelected);
        result.StatusText.Should().Be("Select at least one column.");
        result.Plan.Should().BeNull();
    }

    [Fact]
    public void SelectAllAndClearAll_PreserveOffsetsAndLabels()
    {
        RemoveDuplicateColumnChoice[] choices =
        [
            new(0, "Region", false),
            new(1, "Rep", true),
        ];

        RemoveDuplicatesPlanner.SelectAll(choices).Should().Equal(
            new RemoveDuplicateColumnChoice(0, "Region", true),
            new RemoveDuplicateColumnChoice(1, "Rep", true));
        RemoveDuplicatesPlanner.ClearAll(choices).Should().Equal(
            new RemoveDuplicateColumnChoice(0, "Region", false),
            new RemoveDuplicateColumnChoice(1, "Rep", false));
    }

    [Fact]
    public void GetSelectedColumnOffsets_ReturnsSelectedOffsetsInDisplayOrder()
    {
        RemoveDuplicatesPlanner.GetSelectedColumnOffsets(
            [
                new RemoveDuplicateColumnChoice(2, "Amount", true),
                new RemoveDuplicateColumnChoice(0, "Region", false),
                new RemoveDuplicateColumnChoice(1, "Rep", true),
            ])
            .Should()
            .Equal(2u, 1u);
    }

    private static Workbook CreateWorkbook()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        return workbook;
    }

    private static CellAddress Address(Sheet sheet, uint row, uint col) =>
        new(sheet.Id, row, col);

    private static GridRange Range(Sheet sheet, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(new CellAddress(sheet.Id, startRow, startCol), new CellAddress(sheet.Id, endRow, endCol));
}
