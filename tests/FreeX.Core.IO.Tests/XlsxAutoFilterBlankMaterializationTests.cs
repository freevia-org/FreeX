using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for finding J30: loading a worksheet-level value-list AutoFilter with
/// blank-inclusion (&lt;filters blank="1"&gt;) must carry that inclusion into
/// <see cref="Sheet.ActiveValueFilterColumns"/> — via the same literal "" sentinel
/// FreeX.Core.Commands.FilterValueFormatter.ToText uses for a blank cell (and that the interactive
/// checklist's "(Blanks)" entry also uses) — so that a later, unrelated FilterCommand.RecomputeHiddenRows
/// (triggered by applying/clearing a filter on any OTHER column, which rebuilds hidden rows for every
/// active column from ActiveValueFilterColumns with zero blank-awareness) does not re-hide blank rows
/// the saved filter explicitly meant to keep visible.
/// </summary>
public sealed class XlsxAutoFilterBlankMaterializationTests
{
    [Fact]
    public void WorksheetAutoFilter_ValueListWithIncludeBlank_RegistersBlankSentinelInActiveValueFilterColumns()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("X"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), BlankValue.Instance);
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Y"));

        sheet.AutoFilter = new WorksheetAutoFilterModel("A1:A4", null);
        sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(0, ["X"], IncludeBlank: true));

        XlsxWorksheetAutoFilterMaterializer.MaterializeFilters(sheet);

        // At load, the blank row (row 3) is correctly kept visible: only row 4 ("Y") fails the filter.
        sheet.FilterHiddenRows.Should().BeEquivalentTo([4u]);

        // The registered allowed-values set must explicitly represent the blank via the "" sentinel
        // (the same convention FilterValueFormatter.ToText/the interactive checklist use), not just
        // the literal values — otherwise a later recompute over ALL active columns treats blank as
        // disallowed and re-hides row 3.
        sheet.ActiveValueFilterColumns.Should().ContainKey(1);
        sheet.ActiveValueFilterColumns[1].Should().BeEquivalentTo(["X", ""]);
    }

    [Fact]
    public void WorksheetAutoFilter_ValueListWithoutIncludeBlank_DoesNotRegisterBlankSentinel()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("X"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), BlankValue.Instance);
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Y"));

        sheet.AutoFilter = new WorksheetAutoFilterModel("A1:A4", null);
        sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(0, ["X"], IncludeBlank: false));

        XlsxWorksheetAutoFilterMaterializer.MaterializeFilters(sheet);

        // Blank row is correctly hidden at load since the filter does not allow blanks.
        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u, 4u]);

        // No "" sentinel should be added when the saved filter never included blanks.
        sheet.ActiveValueFilterColumns[1].Should().BeEquivalentTo(["X"]);
    }

    [Fact]
    public void WorksheetAutoFilter_ValueListWithIncludeBlank_AlreadyContainingEmptyStringValue_DoesNotDuplicateSentinel()
    {
        // Defensive: if the saved filter's literal Values already happens to contain "" (e.g. Excel
        // itself wrote an explicit empty-string filter value alongside blank="1"), the materializer
        // must not add a second, duplicate "" entry.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), BlankValue.Instance);

        sheet.AutoFilter = new WorksheetAutoFilterModel("A1:A2", null);
        sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(0, ["X", ""], IncludeBlank: true));

        XlsxWorksheetAutoFilterMaterializer.MaterializeFilters(sheet);

        sheet.ActiveValueFilterColumns[1].Should().BeEquivalentTo(["X", ""]);
        sheet.ActiveValueFilterColumns[1].Count.Should().Be(2);
    }
}
