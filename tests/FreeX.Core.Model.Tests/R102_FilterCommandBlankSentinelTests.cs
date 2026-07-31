using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// HIGH-severity finding: applying (or re-applying) a value-list AutoFilter through the checklist
/// dropdown with the '(Blanks)' entry checked used to persist the blank sentinel as a literal empty
/// <c>&lt;filter val=""/&gt;</c> element instead of the schema's <c>blank="1"</c> flag on the parent
/// <c>&lt;filters&gt;</c> element.
///
/// AutoFilterChecklistPlanner.ToFilterText (via <see cref="FilterValueFormatter.ToText"/>) represents
/// a blank cell as the literal sentinel <c>""</c>, and that sentinel travels straight into
/// <see cref="FilterCommand"/>'s
/// <c>_allowedValues</c>. Before this fix, <see cref="FilterCommand.Apply"/> passed that list -- ""
/// included -- verbatim into <c>new WorksheetAutoFilterColumnModel(columnId, allowedValues)</c> /
/// <c>new StructuredTableFilterColumnModel(columnId, allowedValues)</c>, whose 2-arg constructors
/// hard-code <c>IncludeBlank = false</c>. The mappers that serialize these models
/// (XlsxWorksheetAutoFilterXmlMapper/XlsxStructuredTableWriter, in FreeX.Core.IO) emit every entry in
/// <c>Values</c> unconditionally, so the "" sentinel became a literal <c>&lt;filter val=""/&gt;</c>
/// child -- valid per FreeX's own tolerant reader, but not how ECMA-376's CT_Filters schema (or Excel
/// itself) represents "include blank cells": that is exclusively the <c>blank="1"</c> attribute, with
/// no corresponding empty-string &lt;filter&gt; entry.
///
/// The fix (<see cref="FilterCommand"/>'s new <c>SplitBlankSentinel</c> helper) splits the "" sentinel
/// out of <c>Values</c> and sets <c>IncludeBlank=true</c> before either model is constructed, at the
/// single choke point both call sites in that class share. These tests exercise the REAL product entry
/// point (<see cref="FilterCommand"/> itself, exactly as the checklist dropdown drives it) and assert on
/// the actual production model it writes into <c>sheet.AutoFilter.FilterColumns</c> /
/// <c>table.FilterColumns</c> -- the very objects FreeX.Core.IO's mappers serialize verbatim into the
/// saved .xlsx, so a wrong Values/IncludeBlank split here is exactly what would reach the file.
/// </summary>
public sealed class R102_FilterCommandBlankSentinelTests
{
    [Fact]
    public void R102_WorksheetAutoFilter_BlanksChecked_SplitsSentinelIntoIncludeBlankFlag()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Fruit"));
        // Row 3: Category left genuinely blank.
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Veg"));
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));
        sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null);
        var ctx = new TestCommandContext(wb);

        // User leaves "Fruit" and "(Blanks)" checked (the checklist's blank sentinel, per
        // FilterValueFormatter.ToText's BlankValue => ""), unchecks "Veg".
        var filter = new FilterCommand(sheet.Id, range, filterColOffset: 0, allowedValues: ["Fruit", ""]);
        filter.Apply(ctx).Success.Should().BeTrue();

        var column = sheet.AutoFilter.FilterColumns.Should().ContainSingle().Subject;
        column.ColumnId.Should().Be(0);
        // ECMA-376 ground truth: "include blank cells" is the IncludeBlank flag (which
        // XlsxWorksheetAutoFilterXmlMapper serializes as <filters blank="1"/>), never a literal
        // empty-string entry left sitting in Values.
        column.IncludeBlank.Should().BeTrue(
            "the checklist's '(Blanks)' selection must become IncludeBlank=true, not a literal \"\" left in Values");
        column.Values.Should().BeEquivalentTo(["Fruit"]);
        column.Values.Should().NotContain("",
            "a literal empty-string entry in Values would serialize as a non-standard <filter val=\"\"/> element");
    }

    /// <summary>Same defect, structured-table AutoFilter path (FilterCommand.ApplyToStructuredTableIfMatched).</summary>
    [Fact]
    public void R102_TableAutoFilter_BlanksChecked_SplitsSentinelIntoIncludeBlankFlag()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Fruit"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        // Row 3: Category left genuinely blank.
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Veg"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(30));

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2));
        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "T",
            DisplayName = "T",
            Range = range,
            HasAutoFilter = true,
            Columns =
            {
                new StructuredTableColumnModel(1, "Category"),
                new StructuredTableColumnModel(2, "Amount"),
            },
        };
        sheet.StructuredTables.Add(table);
        var ctx = new TestCommandContext(wb);

        var filter = new FilterCommand(sheet.Id, range, filterColOffset: 0, allowedValues: ["Fruit", ""]);
        filter.Apply(ctx).Success.Should().BeTrue();

        var reloadedTable = sheet.StructuredTables.Single(t => t.Id == table.Id);
        var column = reloadedTable.FilterColumns.Should().ContainSingle(fc => fc.ColumnId == 0).Subject;
        column.IncludeBlank.Should().BeTrue(
            "the checklist's '(Blanks)' selection on a table column must become IncludeBlank=true too");
        column.Values.Should().BeEquivalentTo(["Fruit"]);
        column.Values.Should().NotContain("");
    }

    /// <summary>
    /// No-regression sibling: an ordinary value-list filter that never touches '(Blanks)' must keep
    /// producing exactly the same model as before -- IncludeBlank stays false, and every checked value
    /// still lands in Values untouched.
    /// </summary>
    [Fact]
    public void R102_WorksheetAutoFilter_WithoutBlanksChecked_StillProducesPlainValueList()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Fruit"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Veg"));
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1));
        sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null);
        var ctx = new TestCommandContext(wb);

        var filter = new FilterCommand(sheet.Id, range, filterColOffset: 0, allowedValues: ["Fruit", "Veg"]);
        filter.Apply(ctx).Success.Should().BeTrue();

        var column = sheet.AutoFilter.FilterColumns.Should().ContainSingle().Subject;
        column.IncludeBlank.Should().BeFalse();
        column.Values.Should().BeEquivalentTo(["Fruit", "Veg"]);
    }

    /// <summary>
    /// No-regression sibling: clearing a filter (allowedValues empty) must still remove the column's
    /// entry entirely rather than accidentally synthesizing an IncludeBlank-only entry.
    /// </summary>
    [Fact]
    public void R102_WorksheetAutoFilter_ClearingFilter_StillRemovesColumnEntirely()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Fruit"));
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1));
        sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null);
        var ctx = new TestCommandContext(wb);

        new FilterCommand(sheet.Id, range, filterColOffset: 0, allowedValues: ["Fruit", ""]).Apply(ctx).Success.Should().BeTrue();
        sheet.AutoFilter.FilterColumns.Should().ContainSingle();

        new FilterCommand(sheet.Id, range, filterColOffset: 0, allowedValues: []).Apply(ctx).Success.Should().BeTrue();
        sheet.AutoFilter.FilterColumns.Should().BeEmpty();
    }
}
