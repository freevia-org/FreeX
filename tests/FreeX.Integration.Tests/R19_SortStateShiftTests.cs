using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R19-sort-state-persistence-1/2: a persisted sort's addresses must stay in sync with a structural
/// row/column insert or delete.
///
/// (1) <c>CopyStructuredTableWithRange</c> shifted a structured table's <c>Range</c> on insert/delete
/// but forwarded <c>NativeSortStateXml</c> (the table-level &lt;sortState&gt;/&lt;sortCondition&gt; raw
/// XML captured from the table part) completely unmodified, so a saved table's remembered sort range
/// went stale (one row/column off) the moment any structural edit touched the table.
///
/// (2) <c>ShiftSortState</c> (the worksheet-level sort) rebuilt the shifted model's clone from its
/// discrete fields but left <c>NativeXml</c> null whenever the shift actually changed the reference,
/// so the raw round-tripped XML -- and any &lt;extLst&gt; extension content it carried, which has no
/// other representation in the model -- was silently discarded on the very next save.
/// </summary>
public sealed class R19_sortstate_shift_Tests
{
    private static (Workbook Workbook, Sheet Sheet, ICommandContext Context) Setup()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet, new TestCommandContext(workbook));
    }

    private static GridRange Range(Sheet sheet, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(new CellAddress(sheet.Id, startRow, startCol), new CellAddress(sheet.Id, endRow, endCol));

    // persistence-1: inserting a row above a Structured Table whose per-table sortState/sortCondition
    // came from a real xlsx (raw XML in NativeSortStateXml) must shift the sortState's own ref and its
    // sortCondition's ref by the same amount as the table's Range, not leave them pointing one row high.
    [Fact]
    public void InsertRowAboveTable_ShiftsTableSortStateRefAndCondition()
    {
        var (workbook, sheet, ctx) = Setup();
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = Range(sheet, 1, 1, 10, 3),
            NativeSortStateXml =
                "<sortState xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" ref=\"A2:A10\">" +
                "<sortCondition descending=\"1\" ref=\"A2:A10\" />" +
                "</sortState>"
        });

        var command = new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 1);
        command.Apply(ctx).Success.Should().BeTrue();

        var table = sheet.StructuredTables.Should().ContainSingle().Subject;
        table.Range.Should().Be(Range(sheet, 2, 1, 11, 3));

        table.NativeSortStateXml.Should().NotBeNullOrWhiteSpace();
        var sortStateElement = XElement.Parse(table.NativeSortStateXml!);
        sortStateElement.Attribute("ref")!.Value.Should().Be("A3:A11");
        var sortConditionElement = sortStateElement.Elements()
            .Should().ContainSingle(e => e.Name.LocalName == "sortCondition").Subject;
        sortConditionElement.Attribute("ref")!.Value.Should().Be("A3:A11");

        // Undo must restore the original, unshifted native sort payload.
        command.Revert(ctx);
        var restoredTable = sheet.StructuredTables.Should().ContainSingle().Subject;
        var restoredSortState = XElement.Parse(restoredTable.NativeSortStateXml!);
        restoredSortState.Attribute("ref")!.Value.Should().Be("A2:A10");
    }

    // persistence-2: a worksheet-level sortState's NativeXml (including any <extLst> Excel 2010+
    // extension block it carries) must survive a shift that changes its reference, instead of being
    // dropped in favor of a from-scratch element with no extLst support.
    [Fact]
    public void InsertRowAboveSortState_ShiftsReferenceAndRetainsNativeXmlExtLst()
    {
        var (workbook, sheet, ctx) = Setup();
        const string extUri = "{A8765BA9-456A-4DAB-B4F3-ACF838C3D3B5}";
        sheet.SortState = new WorksheetSortStateModel
        {
            Reference = "A2:C10",
            NativeXml =
                "<sortState xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" ref=\"A2:C10\">" +
                "<sortCondition descending=\"1\" ref=\"A2:A10\" />" +
                "<extLst>" +
                $"<ext uri=\"{extUri}\"><x14:sortCondition xmlns:x14=\"http://schemas.microsoft.com/office/spreadsheetml/2009/9/main\" descending=\"1\" ref=\"A2:A10\" /></ext>" +
                "</extLst>" +
                "</sortState>",
            Conditions = { new WorksheetSortConditionModel { Reference = "A2:A10", Descending = true } }
        };

        var command = new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 1);
        command.Apply(ctx).Success.Should().BeTrue();

        sheet.SortState.Should().NotBeNull();
        sheet.SortState!.Reference.Should().Be("A3:C11");
        sheet.SortState.Conditions.Should().ContainSingle().Which.Reference.Should().Be("A3:A11");

        sheet.SortState.NativeXml.Should().NotBeNullOrWhiteSpace();
        var sortStateElement = XElement.Parse(sheet.SortState.NativeXml!);
        sortStateElement.Attribute("ref")!.Value.Should().Be("A3:C11");

        var sortConditionElement = sortStateElement.Elements()
            .Should().ContainSingle(e => e.Name.LocalName == "sortCondition").Subject;
        sortConditionElement.Attribute("ref")!.Value.Should().Be("A3:A11");

        // The extLst extension block has no modeled representation anywhere else, so it must be
        // preserved verbatim (its own inner ref stays untouched -- only the base-schema attributes are
        // shifted -- but the block itself must not disappear).
        var extLst = sortStateElement.Elements().Should().ContainSingle(e => e.Name.LocalName == "extLst").Subject;
        var ext = extLst.Elements().Should().ContainSingle(e => e.Name.LocalName == "ext").Subject;
        ext.Attribute("uri")!.Value.Should().Be(extUri);

        // Undo must restore the original reference and native payload.
        command.Revert(ctx);
        sheet.SortState.Should().NotBeNull();
        sheet.SortState!.Reference.Should().Be("A2:C10");
        var restoredElement = XElement.Parse(sheet.SortState.NativeXml!);
        restoredElement.Attribute("ref")!.Value.Should().Be("A2:C10");
        restoredElement.Elements().Should().Contain(e => e.Name.LocalName == "extLst");
    }
}
