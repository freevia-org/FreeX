using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R84-io-tables-listobject-5-1: sorting an Excel Table's data (e.g. via the table's own
/// column-dropdown "Sort Largest to Smallest", which routes through SortCommand with the header
/// row excluded — see MainWindow.DataFilterCommands.ApplyAutoFilterDialogResult /
/// ExcludeHeaderRowForAutoFilterSort) must persist the sort into the table's OWN
/// &lt;sortState&gt; (StructuredTableModel.NativeSortStateXml), matching what real Excel writes
/// inside xl/tables/tableN.xml. Previously SortCommand.Apply unconditionally wrote
/// sheet.SortState instead — a worksheet-root &lt;sortState&gt; sibling that Excel's own writer
/// never produces for a table whose autoFilter lives entirely inside its own table part — so the
/// table's persisted sort indicator was silently lost on save+reopen.
/// </summary>
public sealed class R84_SortCommandTableSortStateTests
{
    private static (Workbook Workbook, Sheet Sheet, ICommandContext Context) Setup()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet, new TestCommandContext(workbook));
    }

    private static GridRange Range(Sheet sheet, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(new CellAddress(sheet.Id, startRow, startCol), new CellAddress(sheet.Id, endRow, endCol));

    private static void SeedTable(Sheet sheet)
    {
        // Header row 1 (A1:C1), data rows 2-6 (A2:C6) — a Table over A1:C6, matching the
        // finding's failure scenario. Column B carries the sort key, unsorted.
        string[] headers = ["Name", "Score", "Extra"];
        for (var col = 1u; col <= 3; col++)
            sheet.SetCell(new CellAddress(sheet.Id, 1, col), new TextValue(headers[col - 1]));

        double[] scores = [30, 10, 50, 20, 40];
        for (var i = 0; i < scores.Length; i++)
        {
            var row = (uint)(2 + i);
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"Row{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(scores[i]));
            sheet.SetCell(new CellAddress(sheet.Id, row, 3), new NumberValue(i));
        }
    }

    [Fact]
    public void Apply_OnTableSort_WritesTableNativeSortState_NotWorksheetSortState()
    {
        var (_, sheet, ctx) = Setup();
        SeedTable(sheet);
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = Range(sheet, 1, 1, 6, 3)
        });

        // Mirrors MainWindow.DataFilterCommands: the dropdown's Sort command range excludes the
        // header row before constructing SortCommand.
        var dataRange = Range(sheet, 2, 1, 6, 3);
        var cmd = new SortCommand(sheet.Id, dataRange, sortByColOffset: 1, ascending: false); // Sort Largest to Smallest on column B
        cmd.Apply(ctx).Success.Should().BeTrue();

        // The data must actually have been reordered descending by column B.
        sheet.GetValue(2, 2).Should().Be(new NumberValue(50));
        sheet.GetValue(6, 2).Should().Be(new NumberValue(10));

        // Bug: previously this was NOT null — a worksheet-root sortState was written even though
        // the only autoFilter here belongs to the table.
        sheet.SortState.Should().BeNull(
            "a table-scoped sort must never grow a sibling worksheet-root <sortState> — Excel's own writer never produces that shape for a table");

        var table = sheet.StructuredTables.Should().ContainSingle().Subject;
        table.NativeSortStateXml.Should().NotBeNullOrWhiteSpace(
            "the table's own <sortState> must record the sort that was just applied");

        var sortStateElement = XElement.Parse(table.NativeSortStateXml!);
        sortStateElement.Name.LocalName.Should().Be("sortState");
        sortStateElement.Attribute("ref")!.Value.Should().Be("A2:C6");

        var condition = sortStateElement.Elements().Should().ContainSingle(e => e.Name.LocalName == "sortCondition").Subject;
        condition.Attribute("ref")!.Value.Should().Be("B2:B6", "column B (offset 1 within A2:C6) is the sort key");
        condition.Attribute("descending")!.Value.Should().Be("1", "the sort was Largest to Smallest");
    }

    [Fact]
    public void Revert_OnTableSort_RestoresPriorNativeSortStateXml()
    {
        var (_, sheet, ctx) = Setup();
        SeedTable(sheet);
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = Range(sheet, 1, 1, 6, 3),
            NativeSortStateXml =
                "<sortState xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" ref=\"A2:C6\">" +
                "<sortCondition ref=\"C2:C6\" />" +
                "</sortState>"
        });

        var dataRange = Range(sheet, 2, 1, 6, 3);
        var cmd = new SortCommand(sheet.Id, dataRange, sortByColOffset: 1, ascending: false);
        cmd.Apply(ctx).Success.Should().BeTrue();

        var sortedTable = sheet.StructuredTables.Should().ContainSingle().Subject;
        var sortedXml = XElement.Parse(sortedTable.NativeSortStateXml!);
        sortedXml.Elements().Should().ContainSingle(e => e.Name.LocalName == "sortCondition")
            .Subject.Attribute("ref")!.Value.Should().Be("B2:B6", "sanity: Apply must have rewritten it to the new sort key");

        cmd.Revert(ctx);

        var restoredTable = sheet.StructuredTables.Should().ContainSingle().Subject;
        var restoredXml = XElement.Parse(restoredTable.NativeSortStateXml!);
        restoredXml.Attribute("ref")!.Value.Should().Be("A2:C6");
        restoredXml.Elements().Should().ContainSingle(e => e.Name.LocalName == "sortCondition")
            .Subject.Attribute("ref")!.Value.Should().Be("C2:C6", "undo must restore the original pre-sort table sortState, not the newly-applied one");
        sheet.SortState.Should().BeNull("undo of a table-scoped sort must not leave a worksheet-root sortState behind either");
    }

    /// <summary>
    /// No-regression sibling: a sort over a plain range that is NOT owned by any Structured Table
    /// (the classic worksheet-autofilter/plain-range case R19 already covers) must keep writing
    /// sheet.SortState exactly as before — even when the sheet happens to have an UNRELATED table
    /// elsewhere, which must not be mistaken for the owner of this sort.
    /// </summary>
    [Fact]
    public void Apply_OnPlainRangeSort_StillWritesWorksheetSortState_EvenWithUnrelatedTablePresent()
    {
        var (_, sheet, ctx) = Setup();

        // An unrelated table living in columns E:F, far from the plain range being sorted.
        for (var row = 1u; row <= 4; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 5), new TextValue($"E{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 6), new NumberValue(row));
        }
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "OtherTable",
            DisplayName = "OtherTable",
            Range = Range(sheet, 1, 5, 4, 6)
        });

        for (uint row = 1; row <= 5; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(6 - row));

        sheet.SortState.Should().BeNull();

        var range = Range(sheet, 1, 1, 5, 1);
        var cmd = new SortCommand(sheet.Id, range, sortByColOffset: 0, ascending: true);
        cmd.Apply(ctx).Success.Should().BeTrue();

        sheet.SortState.Should().NotBeNull("a plain-range sort with no owning table must keep persisting to sheet.SortState");
        sheet.SortState!.Reference.Should().Be("A1:A5");

        var otherTable = sheet.StructuredTables.Should().ContainSingle().Subject;
        otherTable.NativeSortStateXml.Should().BeNullOrWhiteSpace("the unrelated table must be untouched by a sort that doesn't belong to it");
    }
}
