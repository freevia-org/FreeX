using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R90-commands-remove-duplicates-consolidate-5-2: <see cref="RemoveDuplicatesPlanner.GuessHasHeaders"/>
/// previously required at least one NumberValue/DateTimeValue/BoolValue in the row directly under
/// the header to guess "has headers", so an all-text table (e.g. a Name/City contact list) was
/// never detected -- exactly the failure scenario in the finding. These tests drive the real
/// product entry point: RemoveDuplicatesDialog.Planning.cs's <c>GuessHasHeaders</c> is a thin
/// forwarder onto this same planner method, and MainWindow.DataCommands.cs:268 feeds its result
/// straight into <see cref="RemoveDuplicatesPlanner.CreatePlan(GridRange, bool, System.Collections.Generic.IEnumerable{RemoveDuplicateColumnChoice})"/>,
/// so exercising GuessHasHeaders -&gt; CreatePlan end-to-end matches exactly what the UI does.
/// </summary>
public sealed class R90_RemoveDuplicatesGuessHasHeadersAllTextTests
{
    [Fact]
    public void GuessHasHeaders_DetectsHeaderRowInAllTextContactTableWithDuplicateRow()
    {
        // Sheet: A1:B4 = Name/City header over Alice/Paris, Bob/London, Alice/Paris (duplicate).
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var range = Range(sheet, 1, 1, 4, 2);
        sheet.SetCell(Address(sheet, 1, 1), new TextValue("Name"));
        sheet.SetCell(Address(sheet, 1, 2), new TextValue("City"));
        sheet.SetCell(Address(sheet, 2, 1), new TextValue("Alice"));
        sheet.SetCell(Address(sheet, 2, 2), new TextValue("Paris"));
        sheet.SetCell(Address(sheet, 3, 1), new TextValue("Bob"));
        sheet.SetCell(Address(sheet, 3, 2), new TextValue("London"));
        sheet.SetCell(Address(sheet, 4, 1), new TextValue("Alice"));
        sheet.SetCell(Address(sheet, 4, 2), new TextValue("Paris"));

        var hasHeaders = RemoveDuplicatesPlanner.GuessHasHeaders(sheet, range);

        hasHeaders.Should().BeTrue();
    }

    [Fact]
    public void CreatePlan_ExcludesHeaderRowFromActiveRangeWhenGuessHasHeadersDetectsAllTextHeader()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var range = Range(sheet, 1, 1, 4, 2);
        sheet.SetCell(Address(sheet, 1, 1), new TextValue("Name"));
        sheet.SetCell(Address(sheet, 1, 2), new TextValue("City"));
        sheet.SetCell(Address(sheet, 2, 1), new TextValue("Alice"));
        sheet.SetCell(Address(sheet, 2, 2), new TextValue("Paris"));
        sheet.SetCell(Address(sheet, 3, 1), new TextValue("Bob"));
        sheet.SetCell(Address(sheet, 3, 2), new TextValue("London"));
        sheet.SetCell(Address(sheet, 4, 1), new TextValue("Alice"));
        sheet.SetCell(Address(sheet, 4, 2), new TextValue("Paris"));

        // Reproduces MainWindow.DataCommands.cs:268's exact call chain: guess the header, then hand
        // that guess straight into CreatePlan the way the ribbon command does.
        var hasHeaders = RemoveDuplicatesPlanner.GuessHasHeaders(sheet, range);
        var plan = RemoveDuplicatesPlanner
            .CreatePlan(range, hasHeaders, RemoveDuplicatesPlanner.BuildColumnChoices(sheet, range, hasHeaders))
            .Plan!;

        plan.HasHeaders.Should().BeTrue();
        // The header row (row 1) must be excluded from the range the duplicate scan actually
        // operates on, or "Name"/"City" would be compared alongside the real data rows.
        plan.ActiveRange.Start.Row.Should().Be(2);
        plan.ActiveRange.End.Row.Should().Be(4);
    }

    // No-regression sibling: a lone all-text column whose first entry is merely a unique value
    // (not a real header) must still be guessed as headerless -- the fix requires at least two
    // columns to agree before trusting the "label doesn't recur in its own column" signal, so a
    // plain unlabeled list of unique names isn't misdetected as having a header.
    [Fact]
    public void GuessHasHeaders_DoesNotDetectHeaderInSingleAllTextColumnOfUniqueValues()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var range = Range(sheet, 1, 1, 3, 1);
        sheet.SetCell(Address(sheet, 1, 1), new TextValue("Charlie"));
        sheet.SetCell(Address(sheet, 2, 1), new TextValue("Delta"));
        sheet.SetCell(Address(sheet, 3, 1), new TextValue("Echo"));

        RemoveDuplicatesPlanner.GuessHasHeaders(sheet, range).Should().BeFalse();
    }

    // No-regression sibling: when the "header" word actually recurs as a data value in its own
    // column (i.e. it's just another data row, not a label), the fix must not guess a header even
    // though every column is text-typed and there are two-plus columns.
    [Fact]
    public void GuessHasHeaders_DoesNotDetectHeaderWhenFirstRowValuesRecurAsDataBelow()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var range = Range(sheet, 1, 1, 3, 2);
        sheet.SetCell(Address(sheet, 1, 1), new TextValue("Paris"));
        sheet.SetCell(Address(sheet, 1, 2), new TextValue("Alice"));
        sheet.SetCell(Address(sheet, 2, 1), new TextValue("London"));
        sheet.SetCell(Address(sheet, 2, 2), new TextValue("Bob"));
        sheet.SetCell(Address(sheet, 3, 1), new TextValue("Paris"));
        sheet.SetCell(Address(sheet, 3, 2), new TextValue("Alice"));

        RemoveDuplicatesPlanner.GuessHasHeaders(sheet, range).Should().BeFalse();
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
